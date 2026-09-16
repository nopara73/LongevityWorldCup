#requires -Version 7.0
param([string]$BaseUrl = 'https://longevityworldcup.com')

$ErrorActionPreference = 'Stop'
$target = $null
if (-not [uri]::TryCreate($BaseUrl, [UriKind]::Absolute, [ref]$target) -or
    $target.Scheme -notin @('http', 'https') -or $target.AbsolutePath -ne '/' -or
    $target.Query -or $target.Fragment -or $target.UserInfo) {
    Write-Host 'Usage: pwsh -NoProfile -File Scripts/check-seo.ps1 [-BaseUrl https://host[:port]]'
    exit 2
}

# Deliberately manual: no project, npm script, test discovery, or workflow references this file.
$canonicalOrigin = 'https://longevityworldcup.com'
$pagePaths = @('/', '/leaderboard', '/pheno-age')
$failures = 0
$clock = [Diagnostics.Stopwatch]::StartNew()
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.UseCookies = $false
$handler.AutomaticDecompression = [Net.DecompressionMethods]::All
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(10)
$client.MaxResponseContentBufferSize = 4MB
$client.DefaultRequestHeaders.UserAgent.ParseAdd('LWC-ManualSeoCheck/1.0')

function Get-Response([string]$Path, [string]$Method = 'GET') {
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method), [uri]::new($target, $Path))
    $response = $null
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        if ([int]$response.StatusCode -ne 200) {
            throw "expected HTTP 200, got $([int]$response.StatusCode); Location: $($response.Headers.Location)"
        }
        $headers = @{}
        foreach ($header in $response.Headers) { $headers[$header.Key] = $header.Value -join ', ' }
        return @{
            Body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            MediaType = $response.Content.Headers.ContentType.MediaType
            Headers = $headers
        }
    }
    catch {
        $cause = $_.Exception
        while ($cause) {
            if ($cause -is [OperationCanceledException] -or $cause -is [TimeoutException]) {
                throw "${Method} ${Path}: request timed out after 10 seconds"
            }
            $cause = $cause.InnerException
        }
        throw "${Method} ${Path}: $($_.Exception.GetBaseException().Message)"
    }
    finally {
        if ($response) { $response.Dispose() }
        $request.Dispose()
    }
}

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Check([string]$Name, [scriptblock]$Action) {
    try {
        & $Action
        Write-Host "PASS $Name"
    }
    catch {
        $script:failures++
        Write-Host "FAIL ${Name}: $($_.Exception.GetBaseException().Message)"
    }
}

function Get-Attribute([string]$Tag, [string]$Name) {
    $pattern = '(?is)\s' + [regex]::Escape($Name) + '\s*=\s*(?:"(?<v>[^"]*)"|''(?<v>[^'']*)''|(?<v>[^\s>]+))'
    return [Net.WebUtility]::HtmlDecode([regex]::Match($Tag, $pattern).Groups['v'].Value)
}

try {
    Write-Host "Manual SEO check: $($target.GetLeftPart([UriPartial]::Authority)) (up to 5 requests; 10s timeout each)"
    $pages = @{}
    foreach ($path in $pagePaths) {
        $page = Get-Response $path
        Require ($page.MediaType -eq 'text/html') "$path expected text/html, got $($page.MediaType)"
        $head = [regex]::Match($page.Body, '(?is)<head\b[^>]*>(.*?)</head>').Groups[1].Value
        Require ([bool]$head) "$path missing HTML head"
        $page.Head = $head
        # Ignore markup inside comments, styles, and scripts when reading metadata.
        $metadata = [regex]::Replace($head, '(?is)<!--.*?-->|<(script|style)\b[^>]*>.*?</\1>', '')
        $page.Tags = @([regex]::Matches($metadata, '(?is)<(?:link|meta)\b(?:[^>''"]|''[^'']*''|"[^"]*")*>') | ForEach-Object Value)
        $pages[$path] = $page
    }
    $sitemap = Get-Response '/sitemap.xml'
    $sitemapHead = Get-Response '/sitemap.xml' 'HEAD'

    Check 'Public status and canonical URLs' {
        foreach ($path in $pagePaths) {
            $links = @($pages[$path].Tags | Where-Object {
                $_ -match '(?i)^<link\b' -and (Get-Attribute $_ 'rel') -match '(?i)(^|\s)canonical(\s|$)'
            })
            Require ($links.Count -eq 1) "$path expected one canonical link, found $($links.Count)"
            $actual = Get-Attribute $links[0] 'href'
            Require ($actual -ceq "$canonicalOrigin$path") "$path canonical should be $canonicalOrigin$path; got '$actual'"
        }
    }

    Check 'HTML and HTTP indexing directives' {
        foreach ($path in $pagePaths) {
            $robots = @($pages[$path].Tags | Where-Object { $_ -match '(?i)^<meta\b' -and (Get-Attribute $_ 'name') -eq 'robots' })
            Require ($robots.Count -eq 1) "$path expected one robots meta tag, found $($robots.Count)"
            $directives = @($pages[$path].Headers['X-Robots-Tag'])
            $directives += @($pages[$path].Tags | Where-Object {
                $_ -match '(?i)^<meta\b' -and (Get-Attribute $_ 'name') -in @('robots', 'googlebot', 'bingbot')
            } | ForEach-Object { Get-Attribute $_ 'content' })
            foreach ($directive in $directives) {
                Require ($directive -notmatch '(?i)\b(noindex|nofollow|none)\b') "$path is public but has restrictive indexing directives: '$directive'"
            }
        }
    }

    Check 'Valid JSON-LD describing each page' {
        foreach ($path in $pagePaths) {
            $nodes = @()
            $scripts = [regex]::Matches([regex]::Replace($pages[$path].Head, '(?s)<!--.*?-->', ''), '(?is)<script\b([^>]*)>(.*?)</script>')
            foreach ($script in $scripts) {
                if ((Get-Attribute "<script $($script.Groups[1].Value)>" 'type') -ne 'application/ld+json') { continue }
                try {
                    # ConvertFrom-Json also accepts comments; JSON-LD requires strict JSON.
                    [System.Text.Json.JsonDocument]::Parse($script.Groups[2].Value).Dispose()
                    $document = ConvertFrom-Json -InputObject $script.Groups[2].Value -AsHashtable -Depth 100
                }
                catch { throw "$path contains invalid JSON-LD: $($_.Exception.Message)" }
                foreach ($root in @($document)) {
                    Require ($root -is [System.Collections.IDictionary]) "$path JSON-LD must contain objects"
                    if ($root.Contains('@graph')) { $nodes += @($root['@graph']) }
                    else { $nodes += $root }
                }
            }
            $pageNodes = @($nodes | Where-Object {
                $_ -is [System.Collections.IDictionary] -and $_['url'] -ceq "$canonicalOrigin$path" -and
                @($_['@type'] | Where-Object { $_ -in @('WebPage', 'CollectionPage', 'ProfilePage') }).Count -gt 0
            })
            Require ($pageNodes.Count -eq 1) "$path expected one JSON-LD page object with url $canonicalOrigin$path; found $($pageNodes.Count)"
            Require (-not [string]::IsNullOrWhiteSpace($pageNodes[0]['name'])) "$path JSON-LD page name is missing"
        }
    }

    Check 'Sampled canonical URLs in sitemap' {
        Require ($sitemap.MediaType -eq 'application/xml') "GET /sitemap.xml expected application/xml, got $($sitemap.MediaType)"
        Require ($sitemap.Body -notmatch '(?i)<!DOCTYPE') '/sitemap.xml must not contain a DTD'
        $xml = [Xml.XmlDocument]::new()
        $xml.XmlResolver = $null
        $xml.LoadXml($sitemap.Body)
        $ns = [Xml.XmlNamespaceManager]::new($xml.NameTable)
        $ns.AddNamespace('s', 'http://www.sitemaps.org/schemas/sitemap/0.9')
        $locations = @($xml.SelectNodes('/s:urlset/s:url/s:loc', $ns) | ForEach-Object InnerText)
        foreach ($path in $pagePaths) {
            $sampleLocations = @($locations | Where-Object { $_ -ceq "$canonicalOrigin$path" })
            Require ($sampleLocations.Count -eq 1) "/sitemap.xml expected $canonicalOrigin$path exactly once; found $($sampleLocations.Count)"
        }
    }

    Check 'Sitemap GET/HEAD consistency' {
        Require ($sitemapHead.MediaType -eq $sitemap.MediaType) '/sitemap.xml GET and HEAD Content-Type differ'
        Require ($sitemapHead.Headers['Cache-Control'] -eq $sitemap.Headers['Cache-Control']) '/sitemap.xml GET and HEAD Cache-Control differ'
        Require ($sitemapHead.Body.Length -eq 0) '/sitemap.xml HEAD unexpectedly returned a body'
    }
}
catch {
    $failures++
    Write-Host "FAIL $($_.Exception.GetBaseException().Message)"
}
finally { $client.Dispose() }

Write-Host "SEO check: $failures failure(s), $($clock.Elapsed.TotalSeconds.ToString('F1', [Globalization.CultureInfo]::InvariantCulture))s"
if ($failures) { exit 1 }
exit 0
