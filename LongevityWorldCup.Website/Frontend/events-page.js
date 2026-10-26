const eventsPageAssets = document.currentScript.dataset;
(function(){
    window.initializeSharedPageData();
    const EVENT_TYPE = { General: 0, Joined: 1, NewRank: 2, DonationReceived: 3, AthleteCountMilestone: 4, BadgeAward: 5, CustomEvent: 6, SeasonFinalResult: 7, LongevitymaxxingChallengeResult: 8, BecamePro: 9, BiologicalAgeImproved: 10, CrowdAgeTop10Change: 11, AgeImprovementTop10Change: 12, TestResultAccepted: 13 };
    const EVENT_TYPE_NAME_BY_ID = {};
    Object.keys(EVENT_TYPE).forEach(name => {
        EVENT_TYPE_NAME_BY_ID[String(EVENT_TYPE[name])] = name;
    });

    function eventTypeName(type){
        return EVENT_TYPE_NAME_BY_ID[String(Number(type))] || "Unknown";
    }

    function buildEventSkeletonRows(count=5){
        return Array.from({ length: count }, () => `
            <tr class="events-skeleton-row" aria-hidden="true">
                <td class="col-avatar"><div class="athlete-td"><span class="events-skeleton-avatar skeleton-shimmer"></span></div></td>
                <td class="event-message"><div class="event-message-cell"><span class="events-skeleton-line is-wide skeleton-shimmer"></span></div></td>
                <td class="col-date"></td>
            </tr>
        `).join("");
    }

    function setEventsLoadingState(preserveExistingRows=false){
        const root = document.getElementById('events-root');
        if (root) root.setAttribute('aria-busy', 'true');
        const status = document.getElementById('eventsStatus');
        if (status) {
            status.setAttribute('role', 'status');
            status.textContent = 'Loading events...';
        }

        const tbody = document.querySelector('#eventsTable tbody');
        if (!tbody) return;
        if (preserveExistingRows && tbody.querySelector('tr.main-row')) return;
        rowObserver?.disconnect();
        resetFeedMore(tbody.closest('table'));
        [...tbody.closest('table').tBodies].slice(1).forEach(body=>body.remove());
        delete tbody.dataset.month;
        tbody.style.removeProperty('--event-month-height');
        monthHeadingObserver?.disconnect();
        tbody.innerHTML = buildEventSkeletonRows();
    }

    function clearEventsLoadingState(message='Events loaded.'){
        const root = document.getElementById('events-root');
        if (root) root.setAttribute('aria-busy', 'false');
        const status = document.getElementById('eventsStatus');
        if (status) status.textContent = message;
    }

    function renderEventsLoadError(retry){
        const tbody = document.querySelector('#eventsTable tbody');
        if (!tbody) return;
        rowObserver?.disconnect();
        resetFeedMore(tbody.closest('table'));
        [...tbody.closest('table').tBodies].slice(1).forEach(body=>body.remove());
        delete tbody.dataset.month;
        tbody.style.removeProperty('--event-month-height');
        monthHeadingObserver?.disconnect();

        const row = document.createElement('tr');
        const cell = document.createElement('td');
        const recovery = document.createElement('div');
        const message = document.createElement('p');
        const retryButton = document.createElement('button');

        cell.colSpan = 3;
        recovery.className = 'events-recovery';
        recovery.setAttribute('role', 'alert');
        message.textContent = 'Events could not load.';
        retryButton.type = 'button';
        retryButton.className = 'events-retry-button';
        retryButton.textContent = 'Retry';
        retryButton.addEventListener('click', retry);

        recovery.append(message, retryButton);
        cell.appendChild(recovery);
        row.appendChild(cell);
        tbody.replaceChildren(row);
    }

    const MILESTONE_COPY = {
        42:    'Answer unlocked - the competition reached <strong class="milestone-count">42</strong> athletes',
        69:    'Nice - the competition reached <strong class="milestone-count">69</strong> athletes',
        100:   'The competition reached <strong class="milestone-count">100</strong> athletes',
        123:   'Counting up, the competition reached <strong class="milestone-count">123</strong> athletes',
        200:   'The competition reached <strong class="milestone-count">200</strong> athletes',
        222:   'Perfectly doubled - the competition reached <strong class="milestone-count">222</strong> athletes',
        250:   'The competition reached <strong class="milestone-count">250</strong> athletes',
        256:   'Power of two - the competition reached <strong class="milestone-count">256</strong> athletes',
        300:   'This is Sparta! The competition reached <strong class="milestone-count">300</strong> athletes',
        404:   'Athlete not found - the competition reached <strong class="milestone-count">404</strong> athletes',
        500:   'The competition reached <strong class="milestone-count">500</strong> athletes',
        666:   'Beast mode - the competition reached <strong class="milestone-count">666</strong> athletes',
        777:   'Lucky roll - the competition reached <strong class="milestone-count">777</strong> athletes',
        888:   'Triple eights - the competition reached <strong class="milestone-count">888</strong> athletes',
        999:   'One short of the comma club - the competition reached <strong class="milestone-count">999</strong> athletes',
        1000:  'The competition reached <strong class="milestone-count">1,000</strong> athletes',
        1024:  'Power-of-two territory - the competition reached <strong class="milestone-count">1,024</strong> athletes',
        1234:  'Counting up nicely - the competition reached <strong class="milestone-count">1,234</strong> athletes',
        1337:  'Leet status - the competition reached <strong class="milestone-count">1,337</strong> athletes',
        1500:  'The competition reached <strong class="milestone-count">1,500</strong> athletes',
        1618:  'Golden ratio vibes - the competition reached <strong class="milestone-count">1,618</strong> athletes',
        2000:  'The competition reached <strong class="milestone-count">2,000</strong> athletes',
        2048:  'Power-of-two territory - the competition reached <strong class="milestone-count">2,048</strong> athletes',
        2222:  'Twos all the way down - the competition reached <strong class="milestone-count">2,222</strong> athletes',
        2500:  'The competition reached <strong class="milestone-count">2,500</strong> athletes',
        3000:  'The competition reached <strong class="milestone-count">3,000</strong> athletes',
        3141:  'Pi energy - the competition reached <strong class="milestone-count">3,141</strong> athletes',
        3333:  'Repeating digits - the competition reached <strong class="milestone-count">3,333</strong> athletes',
        4000:  'The competition reached <strong class="milestone-count">4,000</strong> athletes',
        4444:  'Four-four-four-four and still found - the competition reached <strong class="milestone-count">4,444</strong> athletes',
        5000:  'The competition reached <strong class="milestone-count">5,000</strong> athletes',
        5555:  'Repeating fives - the competition reached <strong class="milestone-count">5,555</strong> athletes',
        6969:  'Double nice - the competition reached <strong class="milestone-count">6,969</strong> athletes',
        7500:  'The competition reached <strong class="milestone-count">7,500</strong> athletes',
        8008:  'Calculator humor survived - the competition reached <strong class="milestone-count">8,008</strong> athletes',
        8888:  'Jackpot-adjacent - the competition reached <strong class="milestone-count">8,888</strong> athletes',
        9000:  'Over nine thousand - the competition reached <strong class="milestone-count">9,000</strong> athletes',
        9001:  'Over nine thousand - the competition reached <strong class="milestone-count">9,001</strong> athletes',
        9999:  'One short of five digits - the competition reached <strong class="milestone-count">9,999</strong> athletes',
        10000: 'The competition reached <strong class="milestone-count">10,000</strong> athletes',
        11111: 'The one key is doing overtime - the competition reached <strong class="milestone-count">11,111</strong> athletes',
        12345: 'Counting is officially a feature - the competition reached <strong class="milestone-count">12,345</strong> athletes',
        22222: 'Twos all the way down again - the competition reached <strong class="milestone-count">22,222</strong> athletes',
        54321: 'Countdown complete, somehow upward - the competition reached <strong class="milestone-count">54,321</strong> athletes',
    };

    function milestoneMessage(n){
        const preset = MILESTONE_COPY[n];
        if (preset) return preset;
        const num = new Intl.NumberFormat().format(n);
        return `The competition reached <strong class="milestone-count">${num}</strong> athletes`;
    }

    function lowerKeyMap(o){const m={};Object.keys(o||{}).forEach(k=>m[k.toLowerCase()]=k);return m;}
    function slugifyLocal(s){return(s||"").toString().normalize("NFD").replace(/[\u0300-\u036f]/g,"").toLowerCase().replace(/[^a-z0-9]+/g,"-").replace(/^-+|-+$/g,"").replace(/-+/g,"-");}
    function normalizeLinkId(id){return String(id).replace(/_/g,"-");}
    function esc(s){
        return String(s).replace(/[&<>"']/g, c => (
            {"&":"&amp;","<":"&lt;","~":"&gt;",'"':"&quot;","'":"&#39;"}[c]
        ).replace("~",">"));
    }
    function initialsFromName(n){const parts=String(n).trim().split(/[\s_-]+/).slice(0,2);return parts.map(p=>p.charAt(0)).join("").toUpperCase()||"?";}

    function normalizeNewlines(s){return window.CustomEventMarkup.normalizeNewlines(s);}
    function splitCustomEventText(text){return window.CustomEventMarkup.splitText(text);}
    function isSafeHttpUrl(value){
        return /^https?:\/\/.+/i.test(String(value||"").trim());
    }
    function getRequestedEventId(){
        try{
            const url=new URL(window.location.href);
            const id=url.searchParams.get("event");
            return id?String(id).trim():"";
        }catch(_){
            return "";
        }
    }
    function renderCustomMarkup(text){return window.CustomEventMarkup.renderWebpageMarkup(text, eventBoardMarkupOptions);}
    function renderCustomMarkupWithBreaks(text){return window.CustomEventMarkup.renderWebpageMarkupWithBreaks(text, eventBoardMarkupOptions);}
    function startOfLocalDay(d){return new Date(d.getFullYear(),d.getMonth(),d.getDate());}
    // The rocket travels with the last word, so it never wraps onto a line of its own.
    function withRocket(label){
        const lastSpace=label.lastIndexOf(' ');
        const head=lastSpace<0?'':label.slice(0,lastSpace+1);
        const tail=label.slice(lastSpace+1);
        return `${head}<span class="event-rocket-tail">${tail}&nbsp;<i class="fa fa-rocket" aria-hidden="true"></i></span>`;
    }
    function relativeDayLabel(iso){
        const dt=new Date(iso); if(isNaN(dt)) return "";
        const today=startOfLocalDay(new Date());
        const thatDay=startOfLocalDay(dt);
        if(thatDay>today) return withRocket('Today');
        const years=today.getFullYear()-thatDay.getFullYear()-((today.getMonth()<thatDay.getMonth()||(today.getMonth()===thatDay.getMonth()&&today.getDate()<thatDay.getDate()))?1:0);
        if(years>=1) return years===1?withRocket('1 year ago'):withRocket(`${years} years ago`);
        const months=(today.getFullYear()*12+today.getMonth())-(thatDay.getFullYear()*12+thatDay.getMonth())-(today.getDate()<thatDay.getDate()?1:0);
        if(months>=1) return months===1?withRocket('1 month ago'):withRocket(`${months} months ago`);
        const diffDays=Math.round((today-thatDay)/86400000);
        if(diffDays<=0) return withRocket('Today');
        if(diffDays===1) return withRocket('Yesterday');
        return withRocket(`${diffDays} days ago`);
    }
    function relativeDayLabelNoIcon(iso,{emptyIfToday=true}={}){
        const dt=new Date(iso); if(isNaN(dt)) return "";
        const today=startOfLocalDay(new Date());
        const thatDay=startOfLocalDay(dt);
        if(thatDay>=today) return emptyIfToday?"" :"Today";
        const years=today.getFullYear()-thatDay.getFullYear()-((today.getMonth()<thatDay.getMonth()||(today.getMonth()===thatDay.getMonth()&&today.getDate()<thatDay.getDate()))?1:0);
        if(years>=1) return years===1?'1 year ago':`${years} years ago`;
        const months=(today.getFullYear()*12+today.getMonth())-(thatDay.getFullYear()*12+thatDay.getMonth())-(today.getDate()<thatDay.getDate()?1:0);
        if(months>=1) return months===1?'1 month ago':`${months} months ago`;
        const diffDays=Math.round((today-thatDay)/86400000);
        if(diffDays===1) return 'Yesterday';
        return `${diffDays} days ago`;
    }
    function formatListDate(iso,{separator=', '}={}){
        const d=new Date(iso); if(isNaN(d)) return '';
        const nowYear=new Date().getFullYear();
        const base=d.toLocaleDateString(undefined,{month:'short',day:'numeric'}).replace(/,/g,'');
        return d.getFullYear()===nowYear?base:`${base}${separator}${d.getFullYear()}`;
    }
    // Chapters use local Gregorian months, with the reader's language and digits.
    // Keep embedded feeds on their existing date format.
    function formatListDateHtml(iso){
        const date=new Date(iso); if(!iso||isNaN(date)) return '';
        const historical=date.getFullYear()!==new Date().getFullYear();
        const formatter=new Intl.DateTimeFormat(undefined,{calendar:'gregory',month:'short',day:'numeric',...(historical?{year:'numeric'}:{})});
        const html=formatter.formatToParts(date).map(part=>`<span class="${part.type==='day'?'event-date-day':'event-date-rest'}">${esc(historical?part.value:part.value.replace(/,/g,''))}</span>`).join('');
        const full=date.toLocaleDateString(undefined,{calendar:'gregory',year:'numeric',month:'long',day:'numeric'});
        return `<time datetime="${esc(iso)}" aria-label="${esc(full)}">${html}</time>`;
    }
    function ordinal(n){const s=["th","st","nd","rd"],v=n%100;return `${n}${s[(v-20)%10]||s[v]||s[0]}`;}
    function podiumIcon(n){
        if(n===1) return '<i class="fa-solid fa-crown rank-icon" style="color: gold;" title="1st place" aria-label="1st place"></i>';
        if(n===2) return '<i class="fa-solid fa-medal rank-icon" style="color: silver;" title="2nd place" aria-label="2nd place"></i>';
        if(n===3) return '<i class="fa-solid fa-award rank-icon" style="color: #cd7f32;" title="3rd place" aria-label="3rd place"></i>';
        return '';
    }
    // A small mark on the portrait says what kind of moment the row is.
    function eventKindMark(r){
        const lost = r.newRankDirection === "lost" || r.badgeDirection === "lost"
            || r.crowdAgeDirection === "lost" || r.improvementDirection === "lost";
        const kinds = {
            [EVENT_TYPE.Joined]: ["joined", "fa-user-plus"],
            [EVENT_TYPE.NewRank]: ["rank", lost ? "fa-arrow-trend-down" : "fa-arrow-trend-up"],
            [EVENT_TYPE.BadgeAward]: ["badge", "fa-award"],
            [EVENT_TYPE.SeasonFinalResult]: ["season", "fa-flag-checkered"],
            [EVENT_TYPE.LongevitymaxxingChallengeResult]: ["challenge", "fa-list-check"],
            [EVENT_TYPE.BecamePro]: ["pro", "fa-star"],
            [EVENT_TYPE.BiologicalAgeImproved]: ["younger", "fa-heart-pulse"],
            [EVENT_TYPE.CrowdAgeTop10Change]: ["crowd", lost ? "fa-arrow-trend-down" : "fa-users"],
            [EVENT_TYPE.AgeImprovementTop10Change]: ["improvement", lost ? "fa-arrow-trend-down" : "fa-chart-line"],
            [EVENT_TYPE.TestResultAccepted]: ["test", "fa-vial"]
        };
        const kind = kinds[r.type];
        if (!kind) return "";
        return `<span class="event-kind" data-kind="${kind[0]}"${lost ? ` data-lost="true"` : ""} aria-hidden="true"><i class="fa-solid ${kind[1]}"></i></span>`;
    }
    function setViewAllVisible(show){const link=document.getElementById("eventsViewAll"); if(link) link.style.display=show?"":"none";}
    function parseTimeVar(varName, fallbackMs){
        const v=getComputedStyle(document.documentElement).getPropertyValue(varName).trim();
        if(!v) return fallbackMs;
        if(v.endsWith('ms')) return parseFloat(v);
        if(v.endsWith('s')) return parseFloat(v)*1000;
        const n=parseFloat(v); return Number.isFinite(n)?n:fallbackMs;
    }

    function buildAthleteIndex(athletes){
        const index=new Map();
        (athletes||[]).forEach(a=>{
            const map=lowerKeyMap(a||{});
            const slugKey=map["athleteslug"]||map["slug"]||map["key"]||map["id"];
            const rawSlug=slugKey?String(a[slugKey]).trim():"";
            if(!rawSlug) return;
            const canon=slugifyLocal(normalizeLinkId(rawSlug));
            index.set(canon, a);
        });
        return index;
    }
    function makeAthleteUrlFromSlug(slug){
        if(!slug) return "";
        return `/athlete/${encodeURIComponent(normalizeLinkId(slugifyLocal(slug)))}`;
    }
    function leaderboardUrl(rank){
        const base="/leaderboard";
        return Number.isFinite(rank)&&rank>0?`${base}#rank-${Math.floor(rank)}`:base;
    }
    function normalizeAthleteNameValue(value){
        if(value===undefined || value===null) return "";
        const resolved=String(value).trim();
        if(!resolved) return "";
        const lowered=resolved.toLowerCase();
        return (lowered==="null" || lowered==="undefined") ? "" : resolved;
    }
    function athleteName(a){
        if(!a) return "";
        const map=lowerKeyMap(a);
        const preferredKeys=[map["displayname"],map["name"],map["athletename"],map["fullname"]].filter(Boolean);
        for(const key of preferredKeys){
            const resolved=normalizeAthleteNameValue(a[key]);
            if(resolved) return resolved;
        }
        return "";
    }
    function resolveMentionName(slug, athletesIndex){
        const key=slugifyLocal(normalizeLinkId(slug));
        const athlete=athletesIndex.get(key);
        if (athlete) return athleteName(athlete)||slug;
        return String(slug||"").replace(/[_-]+/g," ").trim() || slug;
    }
    function athletePic(a){
        if(!a) return "";
        const map=lowerKeyMap(a);
        const keys=["profilepicthumb","profilepicturethumb","thumbnail","thumb","profilepic","profilepicture","profilepictureurl","image","avatar","photo","picture","profileimage","img","imageurl","pictureurl","avatarurl","imgurl","profile_img","profile_pic_url"];
        for(const k of keys){ if(map[k]){ const v=String(a[map[k]]).trim(); if(v) return v; } }
        return "";
    }
    function getCurrentPlacement(a){
        if(!a) return null;
        const map=lowerKeyMap(a);
        const k=map["currentplacement"];
        if(!k) return null;
        const v=Number(a[k]);
        if(!Number.isFinite(v)) return null;
        return Math.max(1,Math.floor(v));
    }

    function extractSlugs(text){
        const rx=/slug\[(.+?)\]/g;
        const out=[]; let m;
        while((m=rx.exec(String(text)))!==null){ out.push(m[1]); }
        return out;
    }
    function extractPrevSlug(text){
        const m=/prev\[(.+?)\]/.exec(String(text));
        return m?m[1]:null;
    }
    function extractRank(text){
        const m=/rank\[(\d+)\]/.exec(String(text));
        if(!m) return null;
        const n=parseInt(m[1],10);
        return Number.isFinite(n)&&n>0?n:null;
    }
    function extractBadge(text){
        const m=/badge\[(.+?)\]/.exec(String(text));
        return m?m[1]:null;
    }
    function extractLeagueCategory(text){
        const m=/cat\[(.+?)\]/.exec(String(text));
        return m?m[1]:null;
    }
    function extractLeagueValue(text){
        const m=/val\[(.*?)\]/.exec(String(text));
        return m?m[1]:null;
    }
    function extractPlace(text){
        const m=/place\[(\d*)\]/.exec(String(text));
        if(!m) return null;
        const n=parseInt(m[1],10);
        return Number.isFinite(n)?n:null;
    }
    function extractSeason(text){
        const m=/season\[(\d+)\]/.exec(String(text));
        if(!m) return null;
        const n=parseInt(m[1],10);
        return Number.isFinite(n)?n:null;
    }

    function extractClock(text){
        const m=/clock\[(.+?)\]/.exec(String(text));
        return m?m[1]:null;
    }

    function extractAgeDiff(text){
        const m=/ageDiff\[(.*?)\]/.exec(String(text));
        if(!m) return null;
        const n=parseFloat(m[1]);
        return Number.isFinite(n)?n:null;
    }

    function extractToken(text, key){
        const escapedKey = String(key||"").replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
        const rx = new RegExp(`${escapedKey}\\[(.*?)\\]`);
        const m = rx.exec(String(text));
        return m ? m[1] : null;
    }

    function extractIntToken(text, key){
        const raw = extractToken(text, key);
        if(raw===null) return null;
        const n = parseInt(raw, 10);
        return Number.isFinite(n) ? n : null;
    }
    function extractFloatToken(text, key){
        const raw = extractToken(text, key);
        if(raw===null) return null;
        const n = parseFloat(raw);
        return Number.isFinite(n) ? n : null;
    }

    function renderTextWithSlugLinks(text, linkNames, athletesIndex, afterSlug){
        const s=String(text);
        const rx=/slug\[(.+?)\]/g;
        let out="", last=0, m;
        while((m=rx.exec(s))!==null){
            out += esc(s.slice(last, m.index));
            const raw=m[1];
            const key=slugifyLocal(normalizeLinkId(raw));
            const a=athletesIndex.get(key);
            const name=a?(athleteName(a)||raw):raw;
            const href=a?makeAthleteUrlFromSlug(raw):"";

            const isSelf = athletePageTargetSlug && key===athletePageTargetSlug;
            const shouldLink = !!href && !isSelf && (linkNames || openLinksInNewTab);
            const modalAttrs = (!openLinksInNewTab && key)
                ? ` class="event-athlete-link" data-athlete-slug="${esc(key)}"`
                : "";

            const targetAttrs = openLinksInNewTab ? ` target="_blank" rel="noopener"` : ``;
            const piece=shouldLink
                ? `<a href="${href}"${modalAttrs}${targetAttrs}>${esc(name)}</a>`
                : `<span class="athlete-name-text">${esc(name)}</span>`;

            const extra=typeof afterSlug==="function"?afterSlug(key,a):"";
            const extraSeparator=extra ? "&nbsp;" : "";
            out += `<span class="name-and-rank">${piece}${extraSeparator}${extra}</span>`;
            last=rx.lastIndex;
        }
        out += esc(s.slice(last));
        return out;
    }

    function findServerBadgeOnAthlete(athlete, label){
        if(!athlete || !label) return null;
        const map=lowerKeyMap(athlete);
        const listKey=map["badges"]||map["athletebadges"]||map["awards"]||map["badge"];
        const list=listKey?athlete[listKey]:null;
        if(!Array.isArray(list)) return null;
        const norm=s=>String(s).trim().toLowerCase();
        const want=norm(label);
        for(const b of list){
            const bm=lowerKeyMap(b);
            const labKey=bm["badgelabel"]||bm["label"]||bm["name"];
            if(labKey && norm(b[labKey])===want) return b;
        }
        return null;
    }

    function findServerBadgeOnAthleteStrict(athlete,label,cat,val,place){
        if(!athlete || !label) return null;
        const map=lowerKeyMap(athlete);
        const listKey=map["badges"]||map["athletebadges"]||map["awards"]||map["badge"];
        const list=listKey?athlete[listKey]:null;
        if(!Array.isArray(list)) return null;

        const norm=s=>String(s||"").trim().toLowerCase();
        const wantL=norm(label);
        const wantC=norm(cat);
        const wantV=norm(val);
        const wantP=place==null?null:Number(place);

        for(const b of list){
            const bm=lowerKeyMap(b);
            const labKey=bm["badgelabel"]||bm["label"]||bm["name"];
            const catKey=bm["leaguecategory"]||bm["category"]||bm["categoryname"]||bm["cat"];
            const valKey=bm["leaguevalue"]||bm["league"]||bm["value"]||bm["val"];
            const plcKey=bm["place"]||bm["rank"]||bm["position"];

            if(!labKey || norm(b[labKey])!==wantL) continue;

            if(cat!=null && String(cat).trim()!==""){
                if(!catKey || norm(b[catKey])!==wantC) continue;
            }

            if(val!=null && String(val).trim()!==""){
                if(!valKey || norm(b[valKey])!==wantV) continue;
            }

            if(wantP!=null){
                if(!plcKey || Number(b[plcKey])!==wantP) continue;
            }

            return b;
        }

        return null;
    }

    function findServerBadgeOnAthleteEx(athlete,label,cat,val,place){
        if(!athlete) return null;
        const map=lowerKeyMap(athlete);
        const listKey=map["badges"]||map["athletebadges"]||map["awards"]||map["badge"];
        const list=listKey?athlete[listKey]:null;
        if(!Array.isArray(list)) return null;
        const norm=s=>String(s||"").trim().toLowerCase();
        const wantL=norm(label),wantC=norm(cat),wantV=norm(val),wantP=place==null?null:Number(place);
        let fallback=null;
        for(const b of list){
            const bm=lowerKeyMap(b);
            const labKey=bm["badgelabel"]||bm["label"]||bm["name"];
            const catKey=bm["leaguecategory"]||bm["category"]||bm["categoryname"]||bm["cat"];
            const valKey=bm["leaguevalue"]||bm["league"]||bm["value"]||bm["val"];
            const plcKey=bm["place"]||bm["rank"]||bm["position"];
            const labOk=labKey?norm(b[labKey])===wantL:false;
            const catOk=wantC?catKey&&norm(b[catKey])===wantC:true;
            const valOk=wantV?valKey&&norm(b[valKey])===wantV:true;
            const plcOk=wantP!=null?plcKey&&Number(b[plcKey])===wantP:true;
            if(labOk&&catOk&&valOk&&plcOk) return b;
            if(labOk&&!fallback) fallback=b;
        }
        return fallback;
    }

    let allowBadgeClicks = true;
    let athletePageTargetSlug = null;
    let openLinksInNewTab = false;
    let eventBoardMarkupOptions = {};

    function renderBadgeBubbleForLabel(label, athlete, leagueCategory=null, leagueValue=null, place=null, links=allowBadgeClicks){
        try{
            const ownedBadge = findServerBadgeOnAthleteStrict(athlete,label,leagueCategory,leagueValue,place);
            const ownedNow = !!ownedBadge;

            const b = ownedBadge ?? { BadgeLabel:String(label), LeagueCategory:leagueCategory, LeagueValue:leagueValue, Place:place };
            const icon=typeof window.pickIconForServerBadge==='function'?window.pickIconForServerBadge(b):'fa-award';
            const rawStyle=typeof window.pickBackgroundForServerBadge==='function'?window.pickBackgroundForServerBadge(b):'';
            const style=typeof window.styleWithBadgeVars==='function'?window.styleWithBadgeVars(rawStyle):rawStyle;
            const tip=typeof window.makeTooltipFromServerBadge==='function'
                ? window.makeTooltipFromServerBadge(b, athlete||null, { suppressValues: !ownedNow })
                : String(label);
            const url=(links && typeof window.pickClickUrl==='function')?window.pickClickUrl(b, athlete||null):'';
            const familyClass=typeof window.getBadgeFamilyClass==='function'?window.getBadgeFamilyClass(b):'badge-family-utility';
            const rarity = window.getBadgeRarity?.(b) || '';
            const rarityAttr = rarity ? ` data-badge-rarity="${rarity}"` : '';
            if(url){
                const isPodcast=String(label||'').trim().toLowerCase()==='podcast';
                const targetAttrs=isPodcast?' target="_blank" rel="noopener"':'';
                const accessibleLabel=isPodcast?`${tip}. Open podcast`:`${tip}. Open league`;
                return `<a class="badge-class ${familyClass} badge-clickable" href="${esc(url)}"${targetAttrs} aria-label="${esc(accessibleLabel)}" title="${esc(tip)}" style="${style}"${rarityAttr}><i class="fa ${icon}" aria-hidden="true"></i></a>`;
            }
            return `<span class="badge-class ${familyClass} badge-explained" role="button" aria-haspopup="dialog" tabindex="0" aria-label="${esc(tip)}" title="${esc(tip)}" style="${style}"${rarityAttr}><i class="fa ${icon}" aria-hidden="true"></i></span>`;
        }catch(e){
            return `<span class="badge-chip"><strong>${esc(String(label))}</strong></span>`;
        }
    }

    function renderBadgeWithFrom(label, prevSlug, athletesIndex, linkNames, afterSlugFn, athlete, forceOpen=false, leagueCategory=null, leagueValue=null, place=null){
        const iconHtml = renderBadgeBubbleForLabel(String(label||"badge"), athlete, leagueCategory, leagueValue, place);
        const cls = forceOpen ? "badge-with-from show-from" : "badge-with-from";
        if(!prevSlug) return `<span class="${cls}">${iconHtml}</span>`;
        const donorHtml = renderTextWithSlugLinks(`slug[${prevSlug}]`, linkNames, athletesIndex, afterSlugFn);
        return `<span class="${cls}">${iconHtml}<span class="badge-from"> from ${donorHtml}</span></span>`;
    }

    function badgeFamilyForItem(item){
        if(typeof window.getBadgeFamilyClass!=="function") return "badge-family-utility";
        return window.getBadgeFamilyClass({
            BadgeLabel:item && item.label,
            LeagueCategory:item && item.leagueCategory,
            LeagueValue:item && item.leagueValue,
            Place:item && item.place
        });
    }

    function pickRepresentativeBadgeItems(items, maxCount=4){
        const source=Array.isArray(items)?items.slice():[];
        if(source.length<=maxCount) return source;

        const picked=[];
        const used=new Set();
        ["badge-family-rank","badge-family-domain","badge-family-pace","badge-family-utility"].forEach(family=>{
            if(picked.length>=maxCount) return;
            const idx=source.findIndex((item,i)=>!used.has(i)&&badgeFamilyForItem(item)===family);
            if(idx>=0){
                picked.push(source[idx]);
                used.add(idx);
            }
        });

        for(let i=0;i<source.length&&picked.length<maxCount;i++){
            if(!used.has(i)){
                picked.push(source[i]);
                used.add(i);
            }
        }

        return picked;
    }

    function toRows(rawEvents){
        return (rawEvents||[]).map(e=>{
            const map=lowerKeyMap(e||{});
            const id=String(e[map["id"]]??e["Id"]??"");
            const type=Number(e[map["type"]]??0);
            const text=String(e[map["text"]]??"");
            const occurredAt=e[map["occurredat"]]??e[map["occurredAt"]]??e["OccurredAt"]??null;
            const relevance=Number(e[map["relevance"]]??e["Relevance"]??0);
            const artwork=e[map["artwork"]]??null;

            const slugTokens=extractSlugs(text).map(s=>slugifyLocal(normalizeLinkId(s)));
            const primarySlug=slugTokens[0]||null;

            const prevRaw=extractPrevSlug(text);
            const prevSlug=prevRaw?slugifyLocal(normalizeLinkId(prevRaw)):null;
            if(prevSlug) slugTokens.push(prevSlug);

            const prevRaws=extractPrevSlugs(text);
            const prevSlugs=prevRaws.map(s=>slugifyLocal(normalizeLinkId(s)));
            for(const ps of prevSlugs) slugTokens.push(ps);

            const isSolo = /solo\[1\]/.test(text);
            let badgeBurstKey = null;
            if(type===EVENT_TYPE.BadgeAward && isSolo && primarySlug){
                const badgeLabel0 = extractBadge(text) || "";
                const leagueCategory0 = extractLeagueCategory(text) || "";
                const leagueValue0 = extractLeagueValue(text) || "";
                const place0 = extractPlace(text);
                badgeBurstKey = `${primarySlug}|solo|${badgeLabel0}|${leagueCategory0}|${leagueValue0}|${place0 ?? ""}`;
            }

            const rank = extractRank(text);
            const sats = (() => { const m = /sats\[(\d+)\]/.exec(text); return m ? parseInt(m[1], 10) : 0; })();
            const milestoneCount = (() => { const m = /athletes\[(\d+)\]/.exec(text); return m ? parseInt(m[1], 10) : null; })();
            const badgeLabel = extractBadge(text);
            const leagueCategory = extractLeagueCategory(text);
            const leagueValue = extractLeagueValue(text);
            const place = extractPlace(text);

            const resultDate = extractToken(text, "date");
            const seasonId = extractSeason(text);
            const clockId = extractClock(text);
            const ageDiff = extractAgeDiff(text);
            const challengeKey = extractToken(text, "challenge");
            const challengeDisplayName = extractToken(text, "name");
            const checkedInDays = extractIntToken(text, "checkedIn");
            const totalPoints = extractIntToken(text, "points");
            const challengeDays = extractIntToken(text, "days");
            const challengeCompleted = /completed\[1\]/.test(text);
            const bioAgeFrom = extractFloatToken(text, "from");
            const bioAgeTo = extractFloatToken(text, "to");
            const previousPlace = extractIntToken(text, "prevPlace");
            const eventCrowdAge = extractFloatToken(text, "crowdAge");
            const eventCrowdCount = extractIntToken(text, "crowdCount");
            const eventImprovement = extractFloatToken(text, "improvement");
            const eventAgeReduction = extractFloatToken(text, "ageReduction");

            return { id, type, text, occurredAt, relevance, artwork, slugs: slugTokens, primarySlug, prevSlug, prevSlugs, isSolo, badgeBurstKey, rank, sats, milestoneCount, badgeLabel, leagueCategory, leagueValue, place, resultDate, seasonId, clockId, ageDiff, challengeKey, challengeDisplayName, checkedInDays, totalPoints, challengeDays, challengeCompleted, bioAgeFrom, bioAgeTo, previousPlace, eventCrowdAge, eventCrowdCount, eventImprovement, eventAgeReduction };
        });
    }

    function isTop10Rank(row, athletesIndex){
        if(row.type !== EVENT_TYPE.NewRank) return true;
        if (Number.isFinite(row.rank)) return row.rank <= 10;
        const a = row.primarySlug ? athletesIndex.get(row.primarySlug) : null;
        const placement = getCurrentPlacement(a);
        return Number.isFinite(placement) && placement <= 10;
    }

    function applyVisibleNow(el){
        const prefersReduced=window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        const rect=el.getBoundingClientRect();
        const vw=window.innerWidth||document.documentElement.clientWidth;
        const vh=window.innerHeight||document.documentElement.clientHeight;
        const inView = rect.bottom>=0 && rect.right>=0 && rect.top<=vh && rect.left<=vw;
        if(inView){
            if(!prefersReduced){
                const idx=Number(el.dataset.rowIndex||0);
                const groupSize=parseInt(getComputedStyle(document.documentElement).getPropertyValue('--row-stagger-group-size'))||8;
                const stepMs=parseTimeVar('--row-stagger-step',40);
                el.style.setProperty('--row-delay',`${(idx%groupSize)*stepMs}`);
            }
            el.classList.add('is-visible');
            if(rowObserver) rowObserver.unobserve(el);
        }
    }

    let rowObserver=null;
    function ensureRowObserver(){
        if(rowObserver) return rowObserver;
        // One observer owns the feed's animation lifecycle. Look up the current
        // table on resize so rerenders neither retain old rows nor add handlers.
        window.addEventListener("resize", () => {
            const table = document.getElementById("eventsTable");
            if (table) revealVisibleRowsImmediately(table);
        }, { passive: true });
        const prefersReduced=window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        rowObserver=new IntersectionObserver((entries,io)=>{
            entries.forEach(entry=>{
                const el=entry.target;
                if(entry.isIntersecting){
                    if(!prefersReduced){
                        const idx=Number(el.dataset.rowIndex||0);
                        const groupSize=parseInt(getComputedStyle(document.documentElement).getPropertyValue('--row-stagger-group-size'))||8;
                        const stepMs=parseTimeVar('--row-stagger-step',40);
                        el.style.setProperty('--row-delay',`${(idx%groupSize)*stepMs}`);
                    }
                    el.classList.add('is-visible');
                    io.unobserve(el);
                }
            });
        },{
            root:null,
            rootMargin:'0px',
            threshold:0
        });
        return rowObserver;
    }

    function revealVisibleRowsImmediately(container){
        container.querySelectorAll('.row-appear:not(.is-visible)').forEach(applyVisibleNow);
    }

    function applyArrivalOrderEpsilon(rows, stepMs=1){
        const seen=new Map();
        for(const r of rows){
            if(!r.occurredAt) continue;
            const t=+new Date(r.occurredAt);
            if(!Number.isFinite(t)) continue;
            const c=seen.get(t)||0;
            if(c>0) {
                // Arrival ordering must not move a displayed date past midnight.
                r.displayOccurredAt = r.occurredAt;
                r.occurredAt = new Date(t+c*stepMs).toISOString();
            }
            seen.set(t,c+1);
        }
        return rows;
    }

    function groupBadgeBursts(rows, windowMs=60000){
        const out=[];
        const removed=new Set();
        for(let i=0;i<rows.length;i++){
            if(removed.has(i)) continue;
            const r=rows[i];
            if(!(r && r.type===EVENT_TYPE.BadgeAward && r.primarySlug && r.occurredAt)){
                out.push(r);
                continue;
            }

            const badgeKey = String(r.badgeLabel||"").trim().toLowerCase();
            if(badgeKey === "podcast"){
                out.push(r);
                continue;
            }

            const groupKey = r.badgeBurstKey || r.primarySlug;

            const t0=+new Date(r.occurredAt);
            if(!Number.isFinite(t0)){ out.push(r); continue; }
            const end=t0+windowMs;

            const idxs=[i];
            const items=[];
            if(r.badgeLabel || r.prevSlug){
                items.push({ label:r.badgeLabel||"badge", prevSlug:r.prevSlug||null, leagueCategory:r.leagueCategory||null, leagueValue:r.leagueValue||null, place:r.place??null });
            }

            for(let j=i+1;j<rows.length;j++){
                if(removed.has(j)) continue;
                const x=rows[j];
                if(!(x && x.type===EVENT_TYPE.BadgeAward && x.primarySlug && x.occurredAt)) continue;

                const xGroupKey = x.badgeBurstKey || x.primarySlug;
                if(xGroupKey !== groupKey) continue;

                const xBadgeKey = String(x.badgeLabel||"").trim().toLowerCase();
                if(xBadgeKey === "podcast") continue;

                const tj=+new Date(x.occurredAt);
                if(!Number.isFinite(tj)) continue;
                if(tj>=t0 && tj<end){
                    idxs.push(j);
                    items.push({ label:x.badgeLabel||"badge", prevSlug:x.prevSlug||null, leagueCategory:x.leagueCategory||null, leagueValue:x.leagueValue||null, place:x.place??null });
                }
                if(tj>=end) break;
            }

            if(idxs.length>1){
                const uniqueLabels=[...new Set(items.map(it=>it.label).filter(Boolean))];
                const groupRow={...rows[i], isGroup:true, badgeCount:idxs.length, badges:uniqueLabels, items, occurredAt: rows[i].occurredAt};
                out.push(groupRow);
                for(let k=1;k<idxs.length;k++) removed.add(idxs[k]);
            }else{
                out.push(r);
            }
        }
        return out;
    }

    function extractPrevSlugs(text){
        const m=/prevs\[(.*?)\]/.exec(String(text));
        if(!m) return [];
        const raw=m[1];
        if(!raw) return [];
        return raw.split(',').map(s=>String(s).trim()).filter(Boolean);
    }

    function groupAthleteBursts(rows, windowMs=60000, respectLocalDays=false){
        const eligibleTypes=new Set([EVENT_TYPE.Joined, EVENT_TYPE.NewRank, EVENT_TYPE.DonationReceived, EVENT_TYPE.BadgeAward, EVENT_TYPE.LongevitymaxxingChallengeResult, EVENT_TYPE.BecamePro, EVENT_TYPE.BiologicalAgeImproved, EVENT_TYPE.CrowdAgeTop10Change, EVENT_TYPE.AgeImprovementTop10Change]);

        const timeOf=r=>{
            const t=+new Date(r && r.occurredAt ? r.occurredAt : 0);
            return Number.isFinite(t) ? t : NaN;
        };

        const out=rows.slice();

        for(let i=0;i<out.length;i++){
            const anchor=out[i];
            if(!(anchor && anchor.primarySlug && eligibleTypes.has(anchor.type) && anchor.occurredAt)) continue;

            const slug=String(anchor.primarySlug);
            const tAnchor=timeOf(anchor);
            if(!Number.isFinite(tAnchor)) continue;
            const anchorDay = respectLocalDays ? +startOfLocalDay(new Date(anchor.displayOccurredAt || anchor.occurredAt)) : null;

            const moved=[];

            for(let j=i-1;j>=0;j--){
                const r=out[j];
                // A dated timeline cannot pull tomorrow's event into yesterday's group.
                if (respectLocalDays && +startOfLocalDay(new Date(r.displayOccurredAt || r.occurredAt)) !== anchorDay) break;
                if(!(r && r.primarySlug && String(r.primarySlug)===slug && eligibleTypes.has(r.type) && r.occurredAt)) continue;

                const tj=timeOf(r);
                if(!Number.isFinite(tj)) continue;

                const diff=tj - tAnchor;
                if(diff>windowMs) break;

                moved.push(r);
                out.splice(j,1);
                i--;
            }

            if(moved.length){
                moved.reverse();
                out.splice(i,0,...moved);
                i += moved.length;
            }
        }

        return out;
    }

    function applyZebraStriping(tbody){
        let idx=0;
        const rows=[...tbody.closest('table').querySelectorAll('tbody tr.main-row')];
        rows.forEach(r=>{
            if(r.classList.contains('details-row')) return;
            r.classList.remove('zebra-even','zebra-odd');
            const cls=(idx%2===0)?'zebra-even':'zebra-odd';
            r.classList.add(cls);
            idx++;
        });
    }

    // The feed groups by month: a month's events sit together, and a repeated
    // day's date stays quiet under the first event of that day.
    // On the Highlights page every month opens with its own heading row.
    let monthHeadingObserver=null;
    function appendMonthHeading(table, group, month){
        const [y,m]=month.split('-').map(Number);
        const date=new Date(0);
        date.setFullYear(y,m-1,1);
        const heading=document.createElement('tr');
        heading.className='events-month-row';
        heading.dataset.month=month;
        heading.innerHTML=`<th colspan="3" scope="rowgroup"><span class="events-month-label">${esc(date.toLocaleDateString(undefined,{calendar:'gregory',month:'long',year:'numeric'}))}</span></th>`;
        group.appendChild(heading);
        monthHeadingObserver?.observe(heading);
        observeMonthReveal(table, heading);
    }

    function markFeedGroups(tbody){
        const chapters=!!tbody.closest('.event-board-page');
        if (chapters) monthRevealObserver?.disconnect();
        if(chapters&&typeof ResizeObserver==='function'){
            monthHeadingObserver??=new ResizeObserver(entries=>entries.forEach(({target})=>{
                target.parentElement?.style.setProperty('--event-month-height',`${target.getBoundingClientRect().height}px`);
            }));
            monthHeadingObserver.disconnect();
        }
        tbody.querySelectorAll('tr.events-month-row').forEach(r=>r.remove());
        const rows=[...tbody.querySelectorAll('tr.main-row')];
        const table=tbody.closest('table');
        let group=tbody;
        let previousDay=null, previousMonth=null;
        rows.forEach((r,i)=>{
            const day=r.dataset.day||'';
            const month=day.split('-').slice(0,2).join('-');
            const nextMonth=(rows[i+1]?.dataset.day||'').split('-').slice(0,2).join('-');
            const dayStarts=!day||day!==previousDay;
            const monthStarts=!month||month!==previousMonth;
            r.classList.toggle('day-start',dayStarts);
            r.classList.toggle('day-repeat',!dayStarts);
            r.classList.toggle('month-start',monthStarts);
            r.classList.toggle('month-end',!month||!rows[i+1]||nextMonth!==month);
            if(chapters&&monthStarts){
                if(i>0){
                    group=document.createElement('tbody');
                    table.appendChild(group);
                }
                if(month) group.dataset.month=month;
                else delete group.dataset.month;
            }
            if(chapters&&monthStarts&&month){
                appendMonthHeading(table, group, month);
            }
            if(chapters){
                const details=r.nextElementSibling?.classList.contains('details-row')?r.nextElementSibling:null;
                group.appendChild(r);
                if(details) group.appendChild(details);
            }
            previousDay=day;
            previousMonth=month;
        });
    }

    // ---- Living feed -------------------------------------------------------
    // The feed behaves like a feed: re-renders keep surviving rows in place and
    // glide them to their new positions, new moments drop in at the top, gone
    // ones fade out, /events grows in chunks behind settling skeletons, months
    // reveal as they arrive and hand the sticky band to the next month, names
    // preview their athlete and the portrait carries over into the profile.
    const FEED_CHUNK = 40;
    const FEED_EASE = 'cubic-bezier(0.2, 0, 0, 1)';
    const FEED_SEEN_KEY = 'lwcEventsSeenAt:v1:';
    const prefersReducedMotion = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    let feedAthletesIndex = new Map();
    const feedLoads = new Map();
    const feedRequests = new WeakMap();

    function readSeenAt(mode){
        try { return Number(window.localStorage.getItem(FEED_SEEN_KEY + mode)) || 0; } catch(_) { return 0; }
    }
    function writeSeenAt(mode, ms){
        try { if (ms > readSeenAt(mode)) window.localStorage.setItem(FEED_SEEN_KEY + mode, String(ms)); } catch(_) {}
    }

    function captureFeedRows(table, nextIds, animate = true){
        const rows = [...table.querySelectorAll('tr.main-row[data-event-id]')];
        if (!rows.length) return null;
        const vh = window.innerHeight || document.documentElement.clientHeight;
        const positions = new Map();
        const ghosts = [];
        let anchor = null;
        rows.forEach(row => {
            const rect = row.getBoundingClientRect();
            const onscreen = rect.bottom > 0 && rect.top < vh && rect.height > 0;
            positions.set(row.dataset.eventId, { top: rect.top, onscreen });
            if (onscreen && !anchor && rect.top >= 0) anchor = { id: row.dataset.eventId, top: rect.top };
            if (animate && onscreen && nextIds && !nextIds.has(row.dataset.eventId)) ghosts.push(makeExitGhost(table, row, rect));
        });
        return { positions, ghosts, anchor, rendered: rows.length };
    }

    // A departing row leaves as a copy laid over its old place, so the rows
    // that stay can already slide into the gap beneath it.
    function makeExitGhost(table, row, rect){
        const ghost = document.createElement('div');
        ghost._feedHost = table.closest('#events-root-index') || document.body;
        ghost.className = 'events-exit-ghost';
        if (table.closest('.event-board-page')) ghost.classList.add('event-board-page');
        ghost.setAttribute('aria-hidden', 'true');
        ghost.inert = true;
        const board = document.createElement('div');
        board.className = 'events-board';
        const ghostTable = document.createElement('table');
        const colgroup = table.querySelector('colgroup');
        if (colgroup) ghostTable.appendChild(colgroup.cloneNode(true));
        const body = document.createElement('tbody');
        const clone = row.cloneNode(true);
        clone.removeAttribute('data-event-id');
        clone.querySelectorAll('[id]').forEach(el => el.removeAttribute('id'));
        body.appendChild(clone);
        ghostTable.appendChild(body);
        board.appendChild(ghostTable);
        ghost.appendChild(board);
        Object.assign(ghost.style, {
            left: `${rect.left + window.scrollX}px`,
            top: `${rect.top + window.scrollY}px`,
            width: `${rect.width}px`,
            height: `${rect.height}px`
        });
        return ghost;
    }

    function settleFeedRerender(table, previous, newIds, animate = true){
        const reduced = !animate || prefersReducedMotion() || document.hidden;
        // Keep the reader's place: the row they were looking at stays put even
        // when something new lands above it.
        if (previous.anchor && window.scrollY > 120) {
            const row = table.querySelector(`tr.main-row[data-event-id="${CSS.escape(previous.anchor.id)}"]`);
            if (row) {
                const delta = row.getBoundingClientRect().top - previous.anchor.top;
                if (Math.abs(delta) > 1) window.scrollBy({ top: delta, behavior: 'instant' });
            }
        }
        const vh = window.innerHeight || document.documentElement.clientHeight;
        let arrivals = 0;
        table.querySelectorAll('tr.main-row[data-event-id]').forEach(row => {
            const before = previous.positions.get(row.dataset.eventId);
            const rect = row.getBoundingClientRect();
            const onscreen = rect.bottom > 0 && rect.top < vh;
            if (before) {
                // A row that stays is already in view: no reveal fade.
                row.style.transition = 'none';
                row.classList.add('is-visible');
                void row.offsetWidth;
                row.style.removeProperty('transition');
                const dy = before.top - rect.top;
                if (!reduced && onscreen && Math.abs(dy) > 1 && typeof row.animate === 'function') {
                    row.animate([{ transform: `translateY(${dy}px)` }, { transform: 'none' }], { duration: 300, easing: FEED_EASE });
                }
            } else if (newIds.has(row.dataset.eventId) && onscreen) {
                markArrival(row, arrivals++ * 70, reduced);
            }
        });
        previous.ghosts.forEach(ghost => {
            if (reduced) return;
            ghost._feedHost.appendChild(ghost);
            const parent = ghost.offsetParent;
            if (parent && parent !== document.body) {
                const origin = parent.getBoundingClientRect();
                ghost.style.left = `${parseFloat(ghost.style.left) - origin.left - window.scrollX - parent.clientLeft + parent.scrollLeft}px`;
                ghost.style.top = `${parseFloat(ghost.style.top) - origin.top - window.scrollY - parent.clientTop + parent.scrollTop}px`;
            }
            const anim = ghost.animate([
                { opacity: 1, transform: 'none' },
                { opacity: 0, transform: 'translateY(6px) scale(.985)' }
            ], { duration: 220, easing: 'cubic-bezier(0.3, 0, 1, 1)', fill: 'forwards' });
            anim.finished.catch(() => {}).finally(() => ghost.remove());
        });
    }

    function markArrival(row, delayMs, reduced){
        row.classList.add('is-visible');
        window.LwcBadges?.celebrate(row);
        if (reduced) return;
        row.style.setProperty('--arrival-delay', `${delayMs}ms`);
        row.classList.add('is-arriving');
        const done = () => {
            row.classList.remove('is-arriving');
            row.style.removeProperty('--arrival-delay');
        };
        window.setTimeout(done, 1900 + delayMs);
    }

    // Months arrive as the reader reaches them.
    let monthRevealObserver = null;
    function observeMonthReveal(table, heading){
        const revealed = table._revealedMonths || (table._revealedMonths = new Set());
        if (revealed.has(heading.dataset.month) || prefersReducedMotion() || typeof IntersectionObserver !== 'function') {
            // A rebuilt band of a month already shown stays still.
            heading.classList.add('is-revealed', 'is-settled');
            return;
        }
        monthRevealObserver ??= new IntersectionObserver(entries => entries.forEach(entry => {
            if (!entry.isIntersecting) return;
            const row = entry.target;
            row.classList.add('is-revealed');
            row.closest('table')?._revealedMonths?.add(row.dataset.month);
            monthRevealObserver.unobserve(row);
        }), { rootMargin: '0px 0px -8% 0px' });
        monthRevealObserver.observe(heading);
        watchMonthHandoff();
    }

    // The pinned month band hands over to the next one: it is marked as pinned,
    // and fades back as the next month pushes it out.
    let monthHandoffWatching = false;
    let queueMonthHandoff = () => {};
    function watchMonthHandoff(){
        if (monthHandoffWatching) return;
        monthHandoffWatching = true;
        let frame = 0;
        const update = () => {
            frame = 0;
            const headings = document.querySelectorAll('.event-board-page tr.events-month-row');
            if (!headings.length) return;
            const stickyTop = parseFloat(getComputedStyle(headings[0]).top) || 0;
            headings.forEach(row => {
                const body = row.parentElement;
                const bodyRect = body.getBoundingClientRect();
                const rect = row.getBoundingClientRect();
                const pinned = bodyRect.top < stickyTop - 1 && bodyRect.bottom > stickyTop;
                const h = rect.height || 40;
                const handoff = pinned ? Math.min(1, Math.max(0, 1 - (bodyRect.bottom - stickyTop - h) / (h * 1.5))) : 0;
                if (row.classList.contains('is-pinned') !== pinned) row.classList.toggle('is-pinned', pinned);
                const value = handoff.toFixed(2);
                if (row.style.getPropertyValue('--handoff') !== value) row.style.setProperty('--handoff', value);
            });
        };
        const queue = () => { if (!frame) frame = requestAnimationFrame(update); };
        queueMonthHandoff = queue;
        window.addEventListener('scroll', queue, { passive: true });
        window.addEventListener('resize', queue, { passive: true });
        queue();
    }

    // /events grows in chunks: skeletons wait at the end and settle into rows.
    function splitIntoUnits(frag){
        const units = [];
        [...frag.childNodes].forEach(node => {
            if (node.classList?.contains('details-row') && units.length) units[units.length - 1].push(node);
            else units.push([node]);
        });
        return units;
    }

    function resetFeedMore(table){
        if (!table) return;
        table._feedPending = [];
        window.clearTimeout(table._feedMoreTimer);
        window.clearTimeout(table._feedIdleTimer);
        table._feedLoadingMore = false;
        table._feedMoreObserver?.disconnect();
        monthRevealObserver?.disconnect();
        table.querySelector('tbody.events-more')?.remove();
    }

    function appendFeedUnits(table, units){
        table.querySelector('tbody.events-more')?.remove();
        let group = [...table.tBodies].at(-1);
        let previous = [...table.querySelectorAll('tr.main-row')].at(-1);
        // Extend the final month without detaching existing rows. Regrouping
        // the whole table loses focus and moves the reader during idle batches.
        units.forEach(unit => {
            const row = unit[0], day = row.dataset.day || '';
            const month = day.split('-').slice(0,2).join('-');
            const previousDay = previous?.dataset.day || '';
            const previousMonth = previousDay.split('-').slice(0,2).join('-');
            const dayStarts = !day || day !== previousDay;
            const monthStarts = !month || month !== previousMonth;
            row.classList.toggle('day-start', dayStarts);
            row.classList.toggle('day-repeat', !dayStarts);
            row.classList.toggle('month-start', monthStarts);
            row.classList.add('month-end');
            if (previous) previous.classList.toggle('month-end', monthStarts);
            if (monthStarts) {
                group = document.createElement('tbody');
                if (month) group.dataset.month = month;
                table.appendChild(group);
                if (month) appendMonthHeading(table, group, month);
            }
            group.append(...unit);
            previous = row;
        });
        applyZebraStriping(group);
    }

    function mountFeedMore(table){
        window.clearTimeout(table._feedIdleTimer);
        table.querySelector('tbody.events-more')?.remove();
        if (!table._feedPending?.length) return;
        const more = document.createElement('tbody');
        more.className = 'events-more';
        more.setAttribute('aria-hidden', 'true');
        more.innerHTML = buildEventSkeletonRows(3);
        table.appendChild(more);
        if (document.hidden) return;
        if (typeof IntersectionObserver !== 'function') { loadFeedMore(table); return; }
        table._feedMoreObserver ??= new IntersectionObserver(entries => {
            if (entries.some(entry => entry.isIntersecting)) loadFeedMore(table);
        }, { rootMargin: '0px 0px 320px 0px' });
        table._feedMoreObserver.disconnect();
        table._feedMoreObserver.observe(more);
        // Finish mounting in quiet batches so browser Find and keyboard users
        // can reach the complete timeline without having to scroll first.
        if (!document.hidden) table._feedIdleTimer = window.setTimeout(() => loadFeedMore(table, true), 1000);
    }

    function loadFeedMore(table, immediate=false){
        if (table._feedLoadingMore || !table._feedPending?.length) return;
        table._feedLoadingMore = true;
        table.querySelector('tbody.events-more')?.classList.add('is-loading');
        const dwell = immediate || prefersReducedMotion() ? 0 : 260;
        table._feedMoreTimer = window.setTimeout(() => {
            table._feedLoadingMore = false;
            const units = table._feedPending.splice(0, FEED_CHUNK);
            if (!units.length) return;
            appendFeedUnits(table, units);
            mountFeedMore(table);
            queueMonthHandoff();
            const fresh = table.querySelectorAll('.row-appear:not(.is-visible)');
            if (prefersReducedMotion()) {
                fresh.forEach(row => row.classList.add('is-visible'));
            } else {
                const io = ensureRowObserver();
                fresh.forEach((row, i) => { row.dataset.rowIndex = String(i); io.observe(row); });
                requestAnimationFrame(() => revealVisibleRowsImmediately(table));
            }
        }, dwell);
    }

    document.addEventListener('keydown', event => {
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'f') {
            document.querySelectorAll('.event-board-page #eventsTable').forEach(table => {
                if (!table._feedPending?.length) return;
                window.clearTimeout(table._feedMoreTimer);
                table._feedLoadingMore = false;
                appendFeedUnits(table, table._feedPending.splice(0));
                mountFeedMore(table);
                table.querySelectorAll('.row-appear').forEach(row => row.classList.add('is-visible'));
            });
        }
    });
    document.addEventListener('focusin', event => {
        const table = event.target.closest?.('.event-board-page #eventsTable');
        if (!table?._feedPending?.length) return;
        const rows = [...table.querySelectorAll('tr.main-row')];
        if (rows.slice(-3).includes(event.target.closest('tr'))) loadFeedMore(table, true);
    });

    // Relative labels ("joined 3 days ago") follow the clock, rolling to the
    // new value instead of waiting for a reload.
    function tickRelativeLabels(){
        const reduced = prefersReducedMotion();
        document.querySelectorAll('.event-rel[data-rel-at]').forEach(el => {
            const next = relativeDayLabel(el.dataset.relAt);
            const current = el.dataset.relHtml ?? el.innerHTML;
            el.dataset.relHtml = next;
            if (reduced) { el.innerHTML = next; return; }
            if (next === current) return;
            el.innerHTML = `<span class="event-rel-out" aria-hidden="true">${current}</span><span class="event-rel-in">${next}</span>`;
            window.setTimeout(() => { if (el.dataset.relHtml === next) el.innerHTML = next; }, 520);
        });
    }
    let relativeTimer = 0, liveTimer = 0;
    function stopFeedClocks(){
        window.clearInterval(relativeTimer);
        window.clearInterval(liveTimer);
        relativeTimer = liveTimer = 0;
        document.querySelectorAll('#eventsTable').forEach(table => {
            window.clearTimeout(table._feedMoreTimer);
            window.clearTimeout(table._feedIdleTimer);
            table._feedLoadingMore = false;
            table._feedMoreObserver?.disconnect();
        });
        document.querySelectorAll('.event-portrait-flight, .events-exit-ghost').forEach(el => {
            el.getAnimations().forEach(animation => animation.cancel());
            el.remove();
        });
        document.querySelectorAll('.is-lifted').forEach(el => el.classList.remove('is-lifted'));
    }
    function startFeedClocks(){
        if (document.hidden || relativeTimer) return;
        relativeTimer = window.setInterval(tickRelativeLabels, 60000);
        liveTimer = window.setInterval(refreshLiveFeeds, 90000);
        document.querySelectorAll('.event-board-page #eventsTable').forEach(mountFeedMore);
        tickRelativeLabels();
    }
    document.addEventListener('visibilitychange', () => {
        if (document.hidden) stopFeedClocks();
        else { startFeedClocks(); refreshLiveFeeds(); }
    });
    window.addEventListener('pagehide', stopFeedClocks);
    window.addEventListener('pageshow', startFeedClocks);
    window.matchMedia('(prefers-reduced-motion: reduce)').addEventListener('change', event => {
        if (!event.matches) return;
        rowObserver?.disconnect();
        monthRevealObserver?.disconnect();
        document.querySelectorAll('.events-board .row-appear').forEach(row => row.classList.add('is-visible'));
        document.querySelectorAll('.events-month-row').forEach(row => row.classList.add('is-revealed', 'is-settled'));
        document.querySelectorAll('.events-board .main-row, .event-athlete-preview, .event-portrait-flight, .events-exit-ghost').forEach(el => {
            el.getAnimations().forEach(animation => animation.cancel());
            el.classList.remove('is-arriving', 'is-launching');
            if (el.matches('.event-portrait-flight, .events-exit-ghost')) el.remove();
        });
        document.querySelectorAll('.is-lifted').forEach(el => el.classList.remove('is-lifted'));
        tickRelativeLabels();
    });

    // New moments find their way in while the page is open.
    let lastLiveRefresh = Date.now();
    function refreshLiveFeeds(force=false){
        if (document.hidden || (!force && Date.now() - lastLiveRefresh < 60000)) return;
        if (athletePreview.isOpen() || athletePreview.isPressing()) return;
        lastLiveRefresh = Date.now();
        feedLoads.forEach((args, key) => {
            const root = document.getElementById(key);
            const board = key === 'events-root' ? root : root?.querySelector('.events-board');
            if (!board || board.getAttribute('aria-busy') === 'true' || board.querySelector('.events-recovery')) return;
            const run = () => window.loadEventsTable(args[0], args[1], args[2], args[3], { ...(args[4] || {}), live: true });
            if (key !== 'events-root' && typeof window.callScoped === 'function') window.callScoped(key, run);
            else run();
        });
    }
    startFeedClocks();
    window.refreshEventsFeed = () => refreshLiveFeeds(true);

    function slugFromAthleteLink(link){
        if (link.dataset.athleteSlug) return link.dataset.athleteSlug;
        const match = /\/athlete\/([^/?#]+)/i.exec(link.getAttribute('href') || '');
        if (!match) return '';
        try { return slugifyLocal(normalizeLinkId(decodeURIComponent(match[1]))); } catch(_) { return ''; }
    }

    // Hovering (or pressing and holding) a name previews the athlete.
    const athletePreview = (function(){
        const SELECTOR = '.events-board a[href*="/athlete/"]:not(.badge-clickable):not(.placement-link)';
        let card = null, anchor = null, showTimer = 0, hideTimer = 0, pressTimer = 0, leaveTimer = 0;
        let pendingPreview = null;
        let pressing = false, pressStart = null, suppressClickFor = null;
        let scrollX = window.scrollX, scrollY = window.scrollY;

        function rememberScroll(){
            scrollX = window.scrollX;
            scrollY = window.scrollY;
        }

        function build(){
            if (card) return card;
            card = document.createElement('div');
            card.id = 'eventAthletePreview';
            card.className = 'event-athlete-preview';
            card.setAttribute('role', 'group');
            card.hidden = true;
            card.addEventListener('pointerenter', () => window.clearTimeout(hideTimer));
            card.addEventListener('pointerleave', event => { if (event.pointerType === 'mouse') scheduleHide(); });
            card.addEventListener('click', event => {
                if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
                if (event.target.closest('.badge-class')) return;
                event.preventDefault();
                const link = anchor;
                if (link?.isConnected) link.click();
            });
            document.body.appendChild(card);
            return card;
        }

        function fill(athlete, slug, link){
            const name = athleteName(athlete) || link.textContent.trim() || slug;
            const map = lowerKeyMap(athlete || {});
            const pic = athlete ? String(athlete[map['profilepicleaderboardthumb']] || athlete[map['profilepic']] || athletePic(athlete) || '') : '';
            const placement = getCurrentPlacement(athlete);
            const meta = [athlete?.[map['flag']], athlete?.[map['division']]].map(v => String(v || '').trim()).filter(Boolean);
            const badgeList = Array.isArray(athlete?.[map['badges']]) ? athlete[map['badges']] : [];
            const badges = badgeList.slice(0, 6).map(b => {
                const bm = lowerKeyMap(b);
                return renderBadgeBubbleForLabel(b[bm['badgelabel']] || b[bm['label']] || '', athlete, b[bm['leaguecategory']] ?? null, b[bm['leaguevalue']] ?? null, b[bm['place']] ?? null, false);
            }).join('');
            const portrait = pic
                ? `<img src="${esc(pic)}" data-lwc-image-role="athlete" data-athlete-name="${esc(name)}" alt="" decoding="async" referrerpolicy="no-referrer">`
                : `<span class="avatar-fallback">${esc(initialsFromName(name))}</span>`;
            card.dataset.slug = slug;
            card.setAttribute('aria-label', name);
            card.innerHTML = `
                <span class="event-athlete-preview-band" aria-hidden="true"></span>
                <span class="event-athlete-preview-portrait">${portrait}</span>
                <a class="event-athlete-preview-name" href="${esc(makeAthleteUrlFromSlug(slug))}">${esc(name)}${placement ? ` <span class="event-athlete-preview-rank">${podiumIcon(placement)}#${placement}</span>` : ''}</a>
                ${meta.length ? `<span class="event-athlete-preview-meta">${meta.map(esc).join(' · ')}</span>` : ''}
                ${badges ? `<span class="event-athlete-preview-badges">${badges}</span>` : ''}`;
            card.querySelector('img')?.addEventListener('error', event => {
                event.target.replaceWith(Object.assign(document.createElement('span'), { className: 'avatar-fallback', textContent: initialsFromName(name) }));
            }, { once: true });
        }

        function place(link){
            const r = link.getBoundingClientRect();
            const vw = document.documentElement.clientWidth;
            const vh = window.innerHeight;
            card.style.maxHeight = '';
            const belowRoom = vh - r.bottom - 18, aboveRoom = r.top - 18;
            const below = card.offsetHeight <= belowRoom || belowRoom >= aboveRoom;
            card.style.maxHeight = `${Math.max(48, Math.min(vh - 16, below ? belowRoom : aboveRoom))}px`;
            const cw = card.offsetWidth, ch = card.offsetHeight;
            const left = Math.min(Math.max(8, r.left + Math.min(r.width, 160) / 2 - cw / 2), vw - cw - 8);
            const top = Math.max(8, Math.min(below ? r.bottom + 10 : r.top - 10 - ch, vh - ch - 8));
            card.dataset.side = below ? 'below' : 'above';
            card.style.left = `${Math.round(left)}px`;
            card.style.top = `${Math.round(top)}px`;
            card.style.setProperty('--preview-origin-x', `${Math.round(Math.min(Math.max(r.left + Math.min(r.width, 160) / 2 - left, 24), cw - 24))}px`);
        }

        function show(link){
            window.clearTimeout(showTimer);
            pendingPreview = null;
            if (!link.isConnected || document.hidden) return;
            rememberScroll();
            const slug = slugFromAthleteLink(link);
            const athlete = slug ? feedAthletesIndex.get(slug) : null;
            if (!athlete) return;
            build();
            window.clearTimeout(hideTimer);
            window.clearTimeout(leaveTimer);
            if (anchor && anchor !== link) anchor.removeAttribute('aria-controls');
            anchor = link;
            if (card.dataset.slug !== slug || card.hidden) fill(athlete, slug, link);
            const wasHidden = card.hidden;
            card.hidden = false;
            card.classList.remove('is-leaving');
            place(link);
            link.setAttribute('aria-controls', card.id);
            if (wasHidden && !prefersReducedMotion()) {
                card.classList.remove('is-entering');
                void card.offsetWidth;
                card.classList.add('is-entering');
            }
        }

        function hide(immediate=false){
            window.clearTimeout(showTimer);
            pendingPreview = null;
            window.clearTimeout(hideTimer);
            window.clearTimeout(leaveTimer);
            cancelPress();
            if (!card || card.hidden) { anchor = null; return; }
            if (card.contains(document.activeElement)) anchor?.focus({ preventScroll: true });
            window.clearTimeout(showTimer);
            pendingPreview = null;
            anchor?.removeAttribute('aria-controls');
            anchor = null;
            if (immediate || prefersReducedMotion()) { card.hidden = true; card.classList.remove('is-entering', 'is-leaving'); return; }
            card.classList.remove('is-entering');
            card.classList.add('is-leaving');
            leaveTimer = window.setTimeout(() => { if (card.classList.contains('is-leaving')) { card.hidden = true; card.classList.remove('is-leaving'); } }, 130);
        }

        function scheduleShow(link, delay, keyboard=false){
            rememberScroll();
            window.clearTimeout(showTimer);
            window.clearTimeout(hideTimer);
            pendingPreview = null;
            if (anchor === link && card && !card.hidden) return;
            pendingPreview = { link, keyboard };
            showTimer = window.setTimeout(() => show(link), card && !card.hidden ? 60 : delay);
        }
        function scheduleHide(){
            // Scrolling a newly focused link into view can also move the pointer
            // away from it. Keyboard intent survives that incidental pointerout.
            if (pendingPreview?.keyboard && pendingPreview.link === document.activeElement) return;
            window.clearTimeout(showTimer);
            pendingPreview = null;
            window.clearTimeout(hideTimer);
            hideTimer = window.setTimeout(() => {
                if (!card?.contains(document.activeElement) && !card?.matches(':hover') && !document.querySelector('.badge-tip:not([hidden]):hover')) hide();
            }, 160);
        }

        document.addEventListener('pointerover', event => {
            if (event.pointerType !== 'mouse') return;
            const link = event.target.closest?.(SELECTOR);
            if (link) scheduleShow(link, 280);
        });
        document.addEventListener('pointerout', event => {
            if (event.pointerType !== 'mouse') return;
            const link = event.target.closest?.(SELECTOR);
            if (link && !link.contains(event.relatedTarget) && !card?.contains(event.relatedTarget)) scheduleHide();
        });
        document.addEventListener('focusin', event => {
            if (card?.contains(event.target)) { window.clearTimeout(hideTimer); return; }
            const link = event.target.closest?.(SELECTOR);
            if (link && link.matches(':focus-visible')) scheduleShow(link, 450, true);
        });
        document.addEventListener('focusout', event => {
            if (event.target.closest?.(SELECTOR) || card?.contains(event.target)) scheduleHide();
        });
        document.addEventListener('pointerdown', event => {
            if (card && !card.hidden && !card.contains(event.target) && !event.target.closest?.(SELECTOR)) hide();
            if (event.pointerType === 'mouse') return;
            suppressClickFor = null;
            const link = event.target.closest?.(SELECTOR);
            if (!link) return;
            rememberScroll();
            pressing = true;
            pressStart = { x: event.clientX, y: event.clientY };
            window.clearTimeout(pressTimer);
            pressTimer = window.setTimeout(() => {
                pressing = false;
                show(link);
                if (anchor === link) suppressClickFor = link;
            }, 420);
        }, { passive: true });
        const cancelPress = () => { pressing = false; window.clearTimeout(pressTimer); };
        document.addEventListener('pointerup', cancelPress, { passive: true });
        document.addEventListener('pointercancel', () => { cancelPress(); suppressClickFor = null; }, { passive: true });
        document.addEventListener('pointermove', event => {
            if (pressing && pressStart && Math.hypot(event.clientX - pressStart.x, event.clientY - pressStart.y) > 10) cancelPress();
        }, { passive: true });
        // A long press shows the preview instead of following the link; the
        // card itself then opens the profile.
        window.addEventListener('click', event => {
            if (!suppressClickFor) return;
            const link = event.target.closest?.(SELECTOR);
            if (link === suppressClickFor) { event.preventDefault(); event.stopImmediatePropagation(); }
            suppressClickFor = null;
        }, true);
        document.addEventListener('contextmenu', event => {
            if (suppressClickFor && event.target.closest?.(SELECTOR) === suppressClickFor) event.preventDefault();
        });
        document.addEventListener('keydown', event => {
            if (!card || card.hidden) return;
            if (event.key === 'Escape' && !document.querySelector('.badge-tip:not([hidden])')) { event.preventDefault(); hide(true); }
            if (event.key !== 'Tab') return;
            const controls = [...card.querySelectorAll('a, [tabindex="0"]')];
            if (!event.shiftKey && event.target === anchor) { event.preventDefault(); controls[0]?.focus(); }
            else if (event.shiftKey && event.target === controls[0]) { event.preventDefault(); anchor?.focus(); }
            else if (!event.shiftKey && event.target === controls.at(-1)) hide(true);
        });
        window.addEventListener('scroll', () => {
            // Scroll notifications can arrive after the gesture that targeted
            // the link. A notification without new movement must not cancel a
            // press or dismiss the preview that was positioned after that scroll.
            if (scrollX === window.scrollX && scrollY === window.scrollY) return;
            rememberScroll();
            const pending = pendingPreview;
            hide(true);
            // A link scrolled into view can receive focus/hover before the scroll
            // event. Preserve that intent only while the link is still targeted;
            // scrolling an already open preview still dismisses it.
            const link = pending?.link;
            const targeted = pending?.keyboard
                ? link === document.activeElement && link.matches(':focus-visible')
                : link?.matches(':hover');
            if (link?.isConnected && targeted) {
                const bounds = link.getBoundingClientRect();
                if (bounds.bottom > 0 && bounds.top < window.innerHeight) scheduleShow(link, pending.keyboard ? 450 : 280, pending.keyboard);
            }
        }, { passive: true });
        window.addEventListener('resize', () => hide(true), { passive: true });
        document.addEventListener('visibilitychange', () => { if (document.hidden) { hide(true); suppressClickFor = null; } });
        window.addEventListener('pagehide', () => { hide(true); suppressClickFor = null; });

        return {
            isOpen: () => !!(card && !card.hidden),
            isPressing: () => pressing,
            portraitFor: slug => (card && !card.hidden && card.dataset.slug === slug) ? card.querySelector('.event-athlete-preview-portrait img') : null,
            hide
        };
    })();

    // Opening a profile from the feed: the portrait lifts out of the row and
    // carries over to where the profile's portrait appears.
    document.addEventListener('lwc:athlete-dialog-before-open', event => {
        const link = event.target;
        if (!(link instanceof Element) || !link.closest('.events-board')) return;
        const slug = slugifyLocal(normalizeLinkId(event.detail?.athleteSlug || slugFromAthleteLink(link)));
        const row = link.closest('tr');
        const source = athletePreview.portraitFor(slug)
            || row?.querySelector(`.avatar-link[data-athlete-slug="${CSS.escape(slug)}"] img.avatar`)
            || (row?.dataset.primarySlug === slug ? row.querySelector('img.avatar') : null);
        const from = source?.getBoundingClientRect();
        athletePreview.hide(true);
        if (row && !prefersReducedMotion()) {
            row.classList.remove('is-launching');
            void row.offsetWidth;
            row.classList.add('is-launching');
            window.setTimeout(() => row.classList.remove('is-launching'), 700);
        }
        if (source && !prefersReducedMotion()) flyPortrait(source, from);
    });

    function flyPortrait(img, from=img.getBoundingClientRect()){
        if (!from.width || !(img.currentSrc || img.src)) return;
        const ghost = document.createElement('img');
        ghost.src = img.currentSrc || img.src;
        ghost.alt = '';
        ghost.className = 'event-portrait-flight';
        ghost.setAttribute('aria-hidden', 'true');
        Object.assign(ghost.style, { left: `${from.left}px`, top: `${from.top}px`, width: `${from.width}px`, height: `${from.height}px` });
        document.body.appendChild(ghost);
        img.classList.add('is-lifted');
        requestAnimationFrame(() => requestAnimationFrame(() => {
            if (!ghost.isConnected || document.hidden || prefersReducedMotion()) { ghost.remove(); img.classList.remove('is-lifted'); return; }
            const target = document.getElementById('modalProfilePic');
            let to = target && target.getClientRects().length ? target.getBoundingClientRect() : null;
            if (!to || to.width < 8 || to.bottom < 0 || to.top > window.innerHeight) {
                const size = Math.min(window.innerWidth, window.innerHeight) * .26;
                to = { left: window.innerWidth / 2 - size / 2, top: window.innerHeight * .2, width: size, height: size };
            }
            const dx = to.left + to.width / 2 - (from.left + from.width / 2);
            const dy = to.top + to.height / 2 - (from.top + from.height / 2);
            const scale = Math.max(1, to.width / from.width);
            const end = `translate(${dx}px, ${dy}px) scale(${scale})`;
            const anim = ghost.animate([
                { transform: 'none', opacity: 1, offset: 0 },
                { transform: `translate(${dx * .5}px, ${dy * .5 - 24}px) scale(${1 + (scale - 1) * .55})`, opacity: 1, offset: .5 },
                { transform: end, opacity: 1, offset: .82 },
                { transform: end, opacity: 0, offset: 1 }
            ], { duration: 460, easing: FEED_EASE, fill: 'forwards' });
            anim.finished.catch(() => {}).finally(() => { ghost.remove(); img.classList.remove('is-lifted'); });
        }));
    }

    function renderRows(rows, athletesIndex, linkNames, options=null){
        const tbody = document.querySelector("#eventsTable tbody");
        const table=tbody.closest('table');
        // Reobserve only the surviving, unrevealed rows after reconciliation.
        rowObserver?.disconnect();
        const chapters=!!tbody.closest('.event-board-page');
        const timelineRows = [...table.querySelectorAll('tr.main-row')];
        const existingRows = new Map(timelineRows.map(row => [row.dataset.eventId, row]));
        for (const unit of table._feedPending || []) existingRows.set(unit[0].dataset.eventId, unit[0]);
        const expandedEvents = new Set(timelineRows.filter(row => row.classList.contains('is-open')).map(row => row.dataset.eventId));
        const active = document.activeElement;
        const focusedRow = active?.closest('tr');
        const focusedMainRow = focusedRow?.classList.contains('details-row') ? focusedRow.previousElementSibling : focusedRow;
        const timelineFocus = timelineRows.includes(focusedMainRow) ? {
            eventId: focusedMainRow.dataset.eventId,
            details: focusedRow !== focusedMainRow,
            href: active.getAttribute('href'),
            label: active.getAttribute('aria-label') || active.textContent,
            index: [...focusedRow.querySelectorAll('a, button')].indexOf(active)
        } : null;
        const nextIds = new Set((Array.isArray(rows) ? rows : []).map(r => r && r.id ? String(r.id) : '').filter(Boolean));
        const previousFeed = captureFeedRows(table, nextIds, !options?.layoutOnly);
        resetFeedMore(table);
        // Reconcile by event identity. Retained rows carry their controls and
        // open details into the new order, including across month boundaries.
        [...table.tBodies].slice(1).forEach(body=>{
            body.remove();
        });
        tbody.querySelectorAll('tr.events-month-row').forEach(row=>row.remove());
        delete tbody.dataset.month;
        tbody.style.removeProperty('--event-month-height');
        if (chapters) tbody.replaceChildren();

        if (!Array.isArray(rows) || rows.length === 0){
            tbody.innerHTML = `<tr class="events-empty-row"><td colspan="3">No events yet</td></tr>`;
            return;
        }

        const nextNodes = [];
        const prefersReduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        const rowsToRender = rows;

        const afterNameWithCurrentRankFor = () => (slug, aObj) => {
            if (!aObj) return "";
            const placement = getCurrentPlacement(aObj);
            if (!placement) return "";
            const label = `(#${placement})`;

            if (linkNames){
                return `<a href="${leaderboardUrl(placement)}" class="placement-link" target="_top">${esc(label)}</a>`;
            }

            if (openLinksInNewTab){
                return `<a href="${leaderboardUrl(placement)}" class="placement-link" target="_blank" rel="noopener">${esc(label)}</a>`;
            }

            return `<span class="placement-link">${esc(label)}</span>`;
        };

        rowsToRender.forEach((r, localIdx) => {
            const idx = localIdx;
            const occurredAt = r.displayOccurredAt || r.occurredAt;
            const dateLabel = occurredAt ? (chapters?formatListDateHtml(occurredAt):esc(formatListDate(occurredAt))) : "";
            const rel = (r.type === EVENT_TYPE.Joined && occurredAt)
                ? `<span class="event-rel" data-rel-at="${esc(occurredAt)}">${relativeDayLabel(occurredAt)}</span>`
                : "";

            const a = r.primarySlug ? athletesIndex.get(r.primarySlug) : null;
            // Selection bookkeeping can change during fitting without changing
            // the event's content or its already decoded portrait.
            const { homepageHighlightSelection, ...content } = r;
            const signature = JSON.stringify([content, dateLabel, (r.slugs || []).map(slug => athletesIndex.get(slug)), linkNames, openLinksInNewTab, allowBadgeClicks]);
            const retained = existingRows.get(String(r.id));
            if (retained?._feedSignature === signature) {
                retained.dataset.rowIndex = String(idx);
                if (homepageHighlightSelection) retained.dataset.highlightSelection = homepageHighlightSelection;
                else delete retained.dataset.highlightSelection;
                nextNodes.push(retained);
                if (retained._feedDetails) nextNodes.push(retained._feedDetails);
                return;
            }
            const detailsId = `custom-event-details-${encodeURIComponent(String(r.id || idx))}`;

            const isCustomEvent = r.type === EVENT_TYPE.CustomEvent;
            const isLongevitymaxxingResult = r.type === EVENT_TYPE.LongevitymaxxingChallengeResult;
            const name = isCustomEvent
                ? "Longevity World Cup"
                : (a ? (athleteName(a) || r.primarySlug) : (isLongevitymaxxingResult ? (r.challengeDisplayName || "Longevitymaxxing Challenge") : (r.primarySlug || "")));
            const url = r.primarySlug ? makeAthleteUrlFromSlug(r.primarySlug) : "";
            const img = a ? athletePic(a) : "";

            const primaryKey = r.primarySlug ? slugifyLocal(normalizeLinkId(r.primarySlug)) : "";
            const isSelfRow = athletePageTargetSlug && primaryKey===athletePageTargetSlug;

            let avatarHtml = "";
            const canLinkAvatar = (r.type === EVENT_TYPE.Joined || r.type === EVENT_TYPE.NewRank || r.type === EVENT_TYPE.BadgeAward || r.type === EVENT_TYPE.SeasonFinalResult || r.type === EVENT_TYPE.LongevitymaxxingChallengeResult || r.type === EVENT_TYPE.BecamePro || r.type === EVENT_TYPE.BiologicalAgeImproved || r.type === EVENT_TYPE.CrowdAgeTop10Change || r.type === EVENT_TYPE.AgeImprovementTop10Change || r.type === EVENT_TYPE.TestResultAccepted)
                && r.primarySlug
                && url
                && !isSelfRow
                && (linkNames || openLinksInNewTab);
            const avatarLoadingAttrs = idx < 10
                ? `loading="eager" fetchpriority="high"`
                : `loading="lazy" fetchpriority="low"`;

            const avatarTargetAttrs = openLinksInNewTab ? ` target="_blank" rel="noopener"` : ``;
            const avatarModalAttrs = (!openLinksInNewTab && primaryKey)
                ? ` data-athlete-slug="${esc(primaryKey)}"`
                : ``;

            if (r.type === EVENT_TYPE.Joined || r.type === EVENT_TYPE.NewRank || r.type === EVENT_TYPE.BadgeAward || r.type === EVENT_TYPE.SeasonFinalResult || r.type === EVENT_TYPE.BecamePro || r.type === EVENT_TYPE.BiologicalAgeImproved || r.type === EVENT_TYPE.CrowdAgeTop10Change || r.type === EVENT_TYPE.AgeImprovementTop10Change || r.type === EVENT_TYPE.TestResultAccepted || (r.type === EVENT_TYPE.LongevitymaxxingChallengeResult && r.primarySlug)) {
                const portrait = img
                    ? `<img class="avatar" data-lwc-image-role="athlete" src="${esc(img)}" alt="${esc(name)}" ${avatarLoadingAttrs} decoding="async" referrerpolicy="no-referrer">`
                    : `<div class="avatar-fallback" data-lwc-image-role="athlete">${initialsFromName(name)}</div>`;
                const framed = `<span class="event-avatar-frame">${portrait}${eventKindMark(r)}</span>`;
                avatarHtml = canLinkAvatar
                    ? `<a href="${url}" class="avatar-link event-athlete-link" title="${esc(name)}" aria-label="${esc(name)}"${avatarModalAttrs}${avatarTargetAttrs}>${framed}</a>`
                    : `<span class="avatar-disabled" aria-label="${esc(name)}">${framed}</span>`;
            } else if (r.type === EVENT_TYPE.DonationReceived) {
                avatarHtml = `<div class="avatar-fallback donation-gift-avatar" title="Donation" aria-label="Donation"><i class="fa-solid fa-gift" aria-hidden="true"></i></div>`;
            } else if (r.type === EVENT_TYPE.AthleteCountMilestone) {
                avatarHtml = `<div class="avatar-fallback milestone-people-avatar" title="Milestone" aria-label="Milestone"><i class="fa-solid fa-people-group" aria-hidden="true"></i></div>`;
            } else if (r.type === EVENT_TYPE.CustomEvent) {
                const logo = eventsPageAssets.defaultProfilePic;
                avatarHtml = `<span class="avatar-disabled" title="Longevity World Cup" aria-label="Longevity World Cup"><img class="avatar custom-event-avatar" data-lwc-image-role="brand" src="${esc(logo)}" alt="Longevity World Cup" title="Longevity World Cup" ${avatarLoadingAttrs} decoding="async" referrerpolicy="no-referrer"></span>`;
            } else if (r.type === EVENT_TYPE.LongevitymaxxingChallengeResult) {
                avatarHtml = `<div class="avatar-fallback milestone-people-avatar" title="Longevitymaxxing Challenge" aria-label="Longevitymaxxing Challenge"><i class="fa-solid fa-list-check" aria-hidden="true"></i></div>`;
            } else {
                avatarHtml = `<div class="avatar-fallback" title="Event">★</div>`;
            }

            let msgHtml = "";
            let overflowBadgeHtml = '';

            if (r.type === EVENT_TYPE.BadgeAward && r.primarySlug) {
                const nameWithRank = renderTextWithSlugLinks(
                    `slug[${r.primarySlug}]`,
                    linkNames,
                    athletesIndex,
                    afterNameWithCurrentRankFor()
                );

                const isLost = r.badgeDirection === "lost";
                const isSolo = !isLost && (r.badgeDirection === "solo" || !!r.isSolo);

                const badgeKey = String(r.badgeLabel||"").trim().toLowerCase();
                if(!isLost && !isSolo && badgeKey === "podcast"){
                    const podcastUrl = (function(){
                        if(!a) return "";
                        const am = lowerKeyMap(a);
                        const key = am["podcastlink"] || am["podcasturl"] || am["podcast"];
                        const v = key ? String(a[key]).trim() : "";
                        return isSafeHttpUrl(v) ? v : "";
                    })();

                    const episodeHtml = podcastUrl
                        ? `<a href="${esc(podcastUrl)}" target="_blank" rel="noopener">episode</a>`
                        : `episode`;

                    msgHtml = `<span class="event-inline-text">${nameWithRank} was featured in a new ${episodeHtml} of our podcast</span>`;
                } else if (
                    (r.isGroup && Number.isFinite(r.badgeCount) && r.badgeCount > 1) ||
                    (Number.isFinite(r.totalBadgeCount) && r.totalBadgeCount > 1)
                ) {
                    const items = Array.isArray(r.allItems)
                        ? r.allItems
                        : ((r.isGroup && Array.isArray(r.items))
                            ? r.items
                            : [{ label:r.badgeLabel||"badge", leagueCategory:r.leagueCategory||null, leagueValue:r.leagueValue||null, place:r.place??null }]);

                    const orderFn = typeof window.computeOrder === 'function' ? window.computeOrder : null;
                    const orderedItems = orderFn
                        ? items.slice().sort((a,b)=>{
                            const oa = orderFn({ BadgeLabel:a.label, LeagueCategory:a.leagueCategory, LeagueValue:a.leagueValue, Place:a.place });
                            const ob = orderFn({ BadgeLabel:b.label, LeagueCategory:b.leagueCategory, LeagueValue:b.leagueValue, Place:b.place });
                            return oa - ob;
                        })
                        : items;

                    const totalCount = Number.isFinite(r.totalBadgeCount) ? r.totalBadgeCount : orderedItems.length;
                    const visibleItems = pickRepresentativeBadgeItems(orderedItems, 4);
                    const icons = visibleItems.map(it => renderBadgeBubbleForLabel(String(it.label || "badge"), a, it.leagueCategory||null, it.leagueValue||null, it.place??null)).join(" ");
                    const moreCount = totalCount - visibleItems.length;
                    overflowBadgeHtml = orderedItems.filter(item => !visibleItems.includes(item))
                        .map(it => renderBadgeBubbleForLabel(String(it.label || 'badge'), a, it.leagueCategory || null, it.leagueValue || null, it.place ?? null, false)).join('');

                    const moreLink = (moreCount > 0 && url)
                        ? ` <a href="${url}" class="badge-class badge-family-utility badge-overflow-count badge-clickable"${openLinksInNewTab ? ` target="_blank" rel="noopener"` : ` target="_top"`} title="${esc(`${moreCount} more badges`)}" aria-label="${esc(`Open ${name} profile to view ${moreCount} more badges`)}">+${moreCount}</a>`
                        : ``;

                    if(isLost){
                        msgHtml = `${nameWithRank} lost <strong class="count-accent">${totalCount}</strong> badges: ${icons}${moreLink}`;
                    }else{
                        msgHtml = `${nameWithRank} bagged <strong class="count-accent">${totalCount}</strong> new badges: ${icons}${moreLink}`;
                    }
                } else {
                    if(isSolo){
                        const bubble = renderBadgeWithFrom(r.badgeLabel, null, athletesIndex, linkNames, afterNameWithCurrentRankFor(), a, true, r.leagueCategory||null, r.leagueValue||null, r.place??null);
                        msgHtml = `${nameWithRank} now holds ${bubble} alone`;
                    } else if(isLost){
                        const bubble = renderBadgeWithFrom(r.badgeLabel, null, athletesIndex, linkNames, afterNameWithCurrentRankFor(), a, true, r.leagueCategory||null, r.leagueValue||null, r.place??null);
                        if(r.otherSlug){
                            const toHtml = renderTextWithSlugLinks(
                                `slug[${r.otherSlug}]`,
                                linkNames,
                                athletesIndex,
                                afterNameWithCurrentRankFor()
                            );
                            msgHtml = `${nameWithRank} lost ${bubble} to ${toHtml}`;
                        }else{
                            msgHtml = `${nameWithRank} lost ${bubble}`;
                        }
                    } else {
                        if (r.prevSlug) {
                            const bubbleWithFrom = renderBadgeWithFrom(r.badgeLabel, r.prevSlug, athletesIndex, linkNames, afterNameWithCurrentRankFor(), a, true, r.leagueCategory||null, r.leagueValue||null, r.place??null);
                            msgHtml = `${nameWithRank} claims ${bubbleWithFrom}`;
                        } else {
                            const bubble = renderBadgeWithFrom(r.badgeLabel, null, athletesIndex, linkNames, afterNameWithCurrentRankFor(), a, true, r.leagueCategory||null, r.leagueValue||null, r.place??null);
                            msgHtml = `${nameWithRank} adds ${bubble} to the collection`;
                        }
                    }
                }
            } else if (r.type === EVENT_TYPE.BecamePro && r.primarySlug) {
                const nameWithRank = renderTextWithSlugLinks(
                    `slug[${r.primarySlug}]`,
                    linkNames,
                    athletesIndex,
                    afterNameWithCurrentRankFor()
                );
                msgHtml = `${nameWithRank} went Pro`;
            } else if (r.type === EVENT_TYPE.TestResultAccepted && r.primarySlug) {
                const nameWithRank = renderTextWithSlugLinks(
                    `slug[${r.primarySlug}]`,
                    linkNames,
                    athletesIndex,
                    afterNameWithCurrentRankFor()
                );
                const testDate = r.resultDate ? ` (${esc(formatListDate(`${r.resultDate}T00:00:00`))})` : "";
                msgHtml = `${nameWithRank} submitted a new test${testDate}`;
            } else if (r.type === EVENT_TYPE.BiologicalAgeImproved && r.primarySlug) {
                const nameWithRank = renderTextWithSlugLinks(
                    `slug[${r.primarySlug}]`,
                    linkNames,
                    athletesIndex,
                    afterNameWithCurrentRankFor()
                );
                const clockNorm = String(r.clockId || "").toLowerCase().replace(/\s+/g,"");
                const clockText = clockNorm === "bortz" || clockNorm === "bortzage" ? "Bortz Age" : "pheno age";
                const fromText = Number.isFinite(r.bioAgeFrom)
                    ? new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 }).format(r.bioAgeFrom)
                    : "?";
                const toText = Number.isFinite(r.bioAgeTo)
                    ? new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 }).format(r.bioAgeTo)
                    : "?";
                msgHtml = `${nameWithRank} improved their ${esc(clockText)} from <strong class="age-years-accent">${esc(fromText)}</strong> to <strong class="age-years-accent">${esc(toText)}</strong> years`;
            } else if (r.type === EVENT_TYPE.CrowdAgeTop10Change && r.primarySlug) {
                const nameWithRank = renderTextWithSlugLinks(
                    `slug[${r.primarySlug}]`,
                    linkNames,
                    athletesIndex,
                    afterNameWithCurrentRankFor()
                );
                const placeN = Number.isFinite(r.place) ? r.place : null;
                const previousPlaceN = Number.isFinite(r.previousPlace) ? r.previousPlace : null;
                const placeText = placeN ? ordinal(placeN) : "?";
                const movementText = previousPlaceN
                    ? (previousPlaceN > placeN ? `climbed from ${ordinal(previousPlaceN)} to ${placeText}` : `moved from ${ordinal(previousPlaceN)} to ${placeText}`)
                    : (placeN === 1 ? `took ${placeText} place` : `entered the top 10 at ${placeText}`);
                const prevHtml = r.prevSlug
                    ? renderTextWithSlugLinks(`slug[${r.prevSlug}]`, linkNames, athletesIndex, afterNameWithCurrentRankFor())
                    : "";
                const crowdAgeText = Number.isFinite(r.eventCrowdAge)
                    ? new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(r.eventCrowdAge)
                    : "?";
                const crowdCountText = Number.isFinite(r.eventCrowdCount)
                    ? new Intl.NumberFormat().format(r.eventCrowdCount)
                    : "?";
                if (r.crowdAgeDirection === "lost") {
                    const toHtml = r.otherSlug
                        ? renderTextWithSlugLinks(`slug[${r.otherSlug}]`, linkNames, athletesIndex, afterNameWithCurrentRankFor())
                        : "";
                    msgHtml = toHtml
                        ? `${nameWithRank} lost the ${esc(placeText)} Crowd Age position to ${toHtml}`
                        : `${nameWithRank} lost the ${esc(placeText)} Crowd Age position`;
                } else {
                    const prevText = prevHtml ? `, ahead&nbsp;of ${prevHtml}` : "";
                    msgHtml = `${nameWithRank} ${esc(movementText)} in Crowd Age${prevText} <span class="event-inline-text event-measurement">(<strong class="age-years-accent">${esc(crowdAgeText)}</strong> years, ${esc(crowdCountText)} guesses)</span>`;
                }
            } else if (r.type === EVENT_TYPE.AgeImprovementTop10Change && r.primarySlug) {
                const nameWithRank = renderTextWithSlugLinks(
                    `slug[${r.primarySlug}]`,
                    linkNames,
                    athletesIndex,
                    afterNameWithCurrentRankFor()
                );
                const placeN = Number.isFinite(r.place) ? r.place : null;
                const previousPlaceN = Number.isFinite(r.previousPlace) ? r.previousPlace : null;
                const placeText = placeN ? ordinal(placeN) : "?";
                const movementText = previousPlaceN
                    ? (previousPlaceN > placeN ? `climbed from ${ordinal(previousPlaceN)} to ${placeText}` : `moved from ${ordinal(previousPlaceN)} to ${placeText}`)
                    : (placeN === 1 ? `took ${placeText} place` : `entered the top 10 at ${placeText}`);
                const clockNorm = String(r.clockId || "").toLowerCase().replace(/\s+/g,"");
                const leaderboardName = clockNorm === "bortz" || clockNorm === "bortzage" ? "Bortz Improvement" : "Pheno Improvement";
                const improvementText = Number.isFinite(r.eventImprovement)
                    ? new Intl.NumberFormat(undefined, { maximumFractionDigits: 1, signDisplay: "exceptZero" }).format(r.eventImprovement)
                    : "?";
                if (r.improvementDirection === "lost") {
                    const toHtml = r.otherSlug
                        ? renderTextWithSlugLinks(`slug[${r.otherSlug}]`, linkNames, athletesIndex, afterNameWithCurrentRankFor())
                        : "";
                    msgHtml = toHtml
                        ? `${nameWithRank} lost the ${esc(placeText)} ${esc(leaderboardName)} position to ${toHtml}`
                        : `${nameWithRank} lost the ${esc(placeText)} ${esc(leaderboardName)} position`;
                } else {
                    const prevHtml = r.prevSlug
                        ? renderTextWithSlugLinks(`slug[${r.prevSlug}]`, linkNames, athletesIndex, afterNameWithCurrentRankFor())
                        : "";
                    const prevText = prevHtml ? `, ahead&nbsp;of ${prevHtml}` : "";
                    msgHtml = `${nameWithRank} ${esc(movementText)} in ${esc(leaderboardName)}${prevText} <span class="event-inline-text event-measurement">(<strong class="age-years-accent">${esc(improvementText)}</strong> years)</span>`;
                }
            } else if (r.type === EVENT_TYPE.NewRank && r.primarySlug) {
                const taken = Number.isFinite(r.rank) ? r.rank : getCurrentPlacement(a);
                const nameWithCurrentRank = renderTextWithSlugLinks(
                    `slug[${r.primarySlug}]`,
                    linkNames,
                    athletesIndex,
                    afterNameWithCurrentRankFor()
                );

                const takenText = Number.isFinite(taken) ? ordinal(taken) : "?";
                const iconHtml  = Number.isFinite(taken) ? podiumIcon(taken) : "";
                const takenHtml = Number.isFinite(taken)
                    ? `<span class="rank-badge"><strong class="event-rank-accent">${esc(takenText)}</strong>${iconHtml}</span>`
                    : esc(takenText);

                if(r.newRankDirection === "lost"){
                    if(r.otherSlug){
                        const toHtml = renderTextWithSlugLinks(
                            `slug[${r.otherSlug}]`,
                            linkNames,
                            athletesIndex,
                            afterNameWithCurrentRankFor()
                        );
                        msgHtml = `${nameWithCurrentRank} lost the ${takenHtml} place to ${toHtml}`;
                    }else{
                        msgHtml = `${nameWithCurrentRank} lost the ${takenHtml} place`;
                    }
                }else{
                    let prevHtml = "";
                    if (r.prevSlug) {
                        prevHtml = renderTextWithSlugLinks(
                            `slug[${r.prevSlug}]`,
                            linkNames,
                            athletesIndex,
                            afterNameWithCurrentRankFor()
                        );
                    }

                    msgHtml = prevHtml
                        ? `${nameWithCurrentRank} took the ${takenHtml} place from ${prevHtml}`
                        : `${nameWithCurrentRank} took the ${takenHtml} place`;
                }
            } else if (r.type === EVENT_TYPE.SeasonFinalResult && r.primarySlug) {
                const nameWithRank = renderTextWithSlugLinks(
                    `slug[${r.primarySlug}]`,
                    linkNames,
                    athletesIndex,
                    afterNameWithCurrentRankFor()
                );

                const placeN = Number.isFinite(r.place) ? r.place : null;
                const placeText = placeN ? ordinal(placeN) : "?";
                const placeIconHtml = placeN ? podiumIcon(placeN) : "";
                const placeHtml = placeN
                    ? `<span class="rank-badge"><strong class="event-rank-accent">${esc(placeText)}</strong>${placeIconHtml}</span>`
                    : esc(placeText);

                const seasonN = Number.isFinite(r.seasonId) ? r.seasonId : null;
                const seasonText = seasonN ? String(seasonN) : "";

                const clockRaw = r.clockId ? String(r.clockId).trim() : "";
                const clockNorm = clockRaw.toLowerCase().replace(/\s+/g,"");
                const clockText = clockNorm === "phenoage" ? "Pheno Age" : (clockRaw || "Pheno Age");

                const ageDiffN = Number.isFinite(r.ageDiff) ? r.ageDiff : null;
                const dir = (ageDiffN !== null && ageDiffN < 0) ? "younger" : "older";
                const years = ageDiffN !== null
                    ? new Intl.NumberFormat(undefined, { minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(Math.abs(ageDiffN))
                    : "?";
                const yearsHtml = `<strong class="age-years-accent">${esc(years)} years</strong>`;

                msgHtml = seasonText
                    ? `${nameWithRank} finished ${placeHtml} in ${esc(seasonText)} with a biological age ${yearsHtml} ${esc(dir)} (${esc(clockText)})`
                    : `${nameWithRank} finished ${placeHtml} with a biological age ${yearsHtml} ${esc(dir)} (${esc(clockText)})`;
            } else if (r.type === EVENT_TYPE.LongevitymaxxingChallengeResult) {
                const participantHtml = r.primarySlug
                    ? renderTextWithSlugLinks(`slug[${r.primarySlug}]`, linkNames, athletesIndex, null)
                    : `<span class="athlete-name-text">${esc(r.challengeDisplayName || "A participant")}</span>`;

                const placeN = Number.isFinite(r.place) ? r.place : null;
                const placeText = placeN ? ordinal(placeN) : "";
                const placeIconHtml = placeN ? podiumIcon(placeN) : "";
                const placeHtml = placeN
                    ? `<span class="rank-badge"><strong class="event-rank-accent">${esc(placeText)}</strong>${placeIconHtml}</span>`
                    : "";

                const checkedIn = Number.isFinite(r.checkedInDays) ? r.checkedInDays : null;
                const challengeDays = Number.isFinite(r.challengeDays) ? r.challengeDays : null;
                const points = Number.isFinite(r.totalPoints) ? r.totalPoints : null;
                // Only the numbers carry the accent; the words between them stay text.
                const checkedInText = checkedIn !== null && challengeDays !== null
                    ? `<strong class="count-accent">${esc(`${checkedIn}/${challengeDays}`)}</strong> checked-in days`
                    : (checkedIn !== null ? `<strong class="count-accent">${esc(String(checkedIn))}</strong> checked-in days` : "");
                const pointsText = points !== null ? `<strong class="count-accent">${esc(String(points))}</strong> points` : "";
                const summary = [checkedInText, pointsText].filter(Boolean).join(" and ");
                const summaryText = summary ? ` with ${summary}` : "";

                if (placeN !== null && placeN <= 3) {
                    msgHtml = `<span class="event-inline-text">${participantHtml} finished ${placeHtml} in the Longevitymaxxing Challenge${summaryText}</span>`;
                } else if (r.challengeCompleted) {
                    msgHtml = `<span class="event-inline-text">${participantHtml} completed the Longevitymaxxing Challenge${summaryText}${placeHtml ? `, finishing ${placeHtml}` : ""}</span>`;
                } else {
                    msgHtml = `<span class="event-inline-text">${participantHtml} finished ${placeHtml} in the Longevitymaxxing Challenge${summaryText}</span>`;
                }
            } else if (r.type === EVENT_TYPE.DonationReceived) {
                const amount = Number.isFinite(r.sats) ? r.sats : 0;
                const btc = amount / 100000000;
                const amountFormatted = new Intl.NumberFormat(undefined, { maximumFractionDigits: 8 }).format(btc);
                msgHtml = `Someone has donated <strong class="donation-amount">${esc(amountFormatted)} BTC</strong>`;
            } else if (r.type === EVENT_TYPE.AthleteCountMilestone) {
                const n = Number.isFinite(r.milestoneCount) ? r.milestoneCount : null;
                msgHtml = n ? milestoneMessage(n) : esc(r.text);
            } else if (r.type === EVENT_TYPE.CustomEvent) {
                const parts = splitCustomEventText(r.text);
                const titleSingle = normalizeNewlines(parts.title).replace(/\n+/g, " ").trim();
                const hasDetails = String(parts.content || "").trim().length > 0 || isSafeHttpUrl(r.artwork?.ImageUrl);

                msgHtml = `<span class="custom-event-title">${renderCustomMarkup(titleSingle)}${
                    hasDetails
                        ? `<button class="custom-event-expander" type="button" aria-expanded="false" aria-controls="${esc(detailsId)}" aria-label="Show details" title="Show details"></button>`
                        : ``
                }</span>`;
            } else {
                const tokenHtml = renderTextWithSlugLinks(
                    r.text,
                    linkNames,
                    athletesIndex,
                    r.type === EVENT_TYPE.Joined ? afterNameWithCurrentRankFor() : null
                );
                msgHtml = r.type === EVENT_TYPE.Joined ? `${tokenHtml} joined ${rel}` : `${tokenHtml}`;
            }

            const tr = retained || document.createElement("tr");
            if (tr._feedToggle) tr.removeEventListener('click', tr._feedToggle);
            tr._feedToggle = null;
            tr._setCustomEventExpanded = null;
            tr._feedDetails = null;
            tr._feedSignature = signature;
            delete tr.dataset.primarySlug;
            delete tr.dataset.highlightSelection;
            delete tr.dataset.day;
            tr.className = "row-appear main-row";
            tr.dataset.rowIndex = String(idx);
            tr.dataset.groupId = `g_${idx}`;
            tr.dataset.eventTypeName = eventTypeName(r.type);
            tr.dataset.badgeEarned = String(r.type === EVENT_TYPE.BadgeAward && r.badgeDirection !== 'lost' && r.badgeDirection !== 'solo' && !r.isSolo);
            if (r.primarySlug) tr.dataset.primarySlug = String(r.primarySlug);
            if (r.homepageHighlightSelection) tr.dataset.highlightSelection = String(r.homepageHighlightSelection);
            if (r.id) tr.dataset.eventId = String(r.id);
            if (occurredAt) {
                const day = new Date(occurredAt);
                if (!isNaN(day)) tr.dataset.day = `${day.getFullYear()}-${day.getMonth() + 1}-${day.getDate()}`;
            }
            tr.innerHTML =
                `<td class="col-avatar"><div class="athlete-td">${avatarHtml}</div></td>
         <td class="event-message"><div class="event-message-cell">${msgHtml}</div></td>
         <td class="col-date">${dateLabel}</td>`;
            const overflowBadge = tr.querySelector('.badge-overflow-count');
            if (overflowBadge && overflowBadgeHtml) window.LwcBadges?.stashOverflow(overflowBadge, overflowBadgeHtml);

            const imgEl = tr.querySelector("img.avatar");
            if (imgEl) {
                const initials = initialsFromName(name);
                imgEl.addEventListener("error", () => {
                    const fallback = document.createElement("div");
                    fallback.className = imgEl.classList.contains("custom-event-avatar") ? "avatar-fallback custom-event-avatar" : "avatar-fallback";
                    const imageRole = imgEl.dataset.lwcImageRole;
                    if (imageRole) fallback.dataset.lwcImageRole = imageRole;
                    fallback.textContent = initials;
                    imgEl.replaceWith(fallback);
                }, { once: true });
            }
            nextNodes.push(tr);

            if (isCustomEvent) {
                tr.classList.add("custom-event-row");

                const parts = splitCustomEventText(r.text);
                const contentTrim = String(parts.content || "").trim();
                const hasDetails = contentTrim.length > 0 || isSafeHttpUrl(r.artwork?.ImageUrl);

                if (!hasDetails) {
                    tr.classList.add("custom-event-no-details");
                    return;
                }

                const contentHtml = renderCustomMarkupWithBreaks(contentTrim);
                const artworkHtml = isSafeHttpUrl(r.artwork?.ImageUrl) && isSafeHttpUrl(r.artwork?.LinkUrl)
                    ? `<a class="custom-event-artwork" href="${esc(r.artwork.LinkUrl)}" target="_top" rel="noopener"><img src="${esc(r.artwork.ImageUrl)}" alt="${esc(r.artwork.AltText || '')}" loading="lazy" decoding="async"></a>` : "";

                const detailsTr = document.createElement("tr");
                detailsTr.className = "details-row custom-event-details";
                if (r.id) detailsTr.dataset.eventId = String(r.id);
                detailsTr.innerHTML =
                    `<td colspan="3" class="details-cell"><div id="${esc(detailsId)}" class="custom-event-content" inert aria-hidden="true"><div class="custom-event-body">${artworkHtml}${contentHtml}</div></div></td>`;
                tr._feedDetails = detailsTr;

                const btn = tr.querySelector(".custom-event-expander");

                const setExpanded = open => {
                    const panel = detailsTr.querySelector('.custom-event-content');
                    if (!open && panel.contains(document.activeElement)) btn?.focus({ preventScroll: true });
                    panel.inert = !open;
                    panel.setAttribute('aria-hidden', String(!open));
                    tr.classList.toggle("is-open", open);
                    if (btn) {
                        btn.setAttribute("aria-expanded", open ? "true" : "false");
                        btn.setAttribute("title", open ? "Hide details" : "Show details");
                        btn.setAttribute("aria-label", open ? "Hide details" : "Show details");
                    }
                    applyZebraStriping(tbody);
                };
                tr._setCustomEventExpanded = setExpanded;
                const toggleOpen = () => setExpanded(!tr.classList.contains('is-open'));

                if (btn) {
                    btn.addEventListener("click", (ev) => {
                        ev.preventDefault();
                        ev.stopPropagation();
                        toggleOpen();
                    });
                }

                tr._feedToggle = (ev) => {
                    const target = ev.target;
                    if (target && target.closest && target.closest("a, button, input, select, textarea")) return;
                    toggleOpen();
                };
                tr.addEventListener('click', tr._feedToggle);

                nextNodes.push(detailsTr);
            }
        });

        if (chapters) {
            // Long timelines render a first chunk; the rest follows on scroll.
            const frag = document.createDocumentFragment();
            frag.append(...nextNodes);
            const units = splitIntoUnits(frag);
            const requestedId = getRequestedEventId();
            const requestedIndex = requestedId ? units.findIndex(unit => unit[0].dataset?.eventId === requestedId) : -1;
            const retainedEnd = units.findLastIndex(unit => previousFeed?.positions.has(unit[0].dataset.eventId));
            const keep = Math.max(FEED_CHUNK, retainedEnd + 1, requestedIndex + 10);
            units.slice(0, keep).forEach(unit => tbody.append(...unit));
            table._feedPending = units.slice(keep);
        } else {
            // Keep unchanged rows and their images attached. Reattaching the
            // whole table restarts image decoding and produces visible flashes.
            let cursor = tbody.firstChild;
            for (const node of nextNodes) {
                if (node === cursor) cursor = cursor.nextSibling;
                else tbody.insertBefore(node, cursor);
            }
            while (cursor) {
                const next = cursor.nextSibling;
                cursor.remove();
                cursor = next;
            }
        }

        // Restore replacement details before any geometry is read. Measuring a
        // briefly collapsed announcement moves the reading anchor and then
        // plays an unsolicited expansion when its open state is restored.
        const renderedTimelineRows = new Map(timelineRows.length
            ? [...table.querySelectorAll('tr.main-row[data-event-id]')].map(row => [row.dataset.eventId, row])
            : []);
        expandedEvents.forEach(id => renderedTimelineRows.get(id)?._setCustomEventExpanded?.(true));

        applyZebraStriping(tbody);
        markFeedGroups(tbody);
        if (chapters) mountFeedMore(table);

        const seenMode = options?.seenMode;
        if (previousFeed) {
            settleFeedRerender(table, previousFeed, nextIds, !options?.layoutOnly);
        } else if (seenMode) {
            // Moments that happened since the last visit drop in at the top.
            const seenAt = readSeenAt(seenMode);
            const vh = window.innerHeight || document.documentElement.clientHeight;
            const reduced = prefersReducedMotion();
            let newest = 0, arrivals = 0;
            rows.forEach(r => {
                const t = +new Date(r.occurredAt || 0);
                if (Number.isFinite(t)) newest = Math.max(newest, t);
            });
            if (seenAt) {
                table.querySelectorAll('tr.main-row[data-event-id]').forEach(row => {
                    const r = rows.find(item => String(item.id) === row.dataset.eventId);
                    const t = r ? +new Date(r.occurredAt || 0) : 0;
                    if (arrivals >= 4 || !(t > seenAt)) return;
                    const rect = row.getBoundingClientRect();
                    if (rect.top > vh) return;
                    markArrival(row, 380 + arrivals++ * 110, reduced);
                });
            }
            if (newest) writeSeenAt(seenMode, newest);
        }

        if (timelineFocus) {
            const mainRow = renderedTimelineRows.get(timelineFocus.eventId);
            const row = timelineFocus.details ? mainRow?.nextElementSibling : mainRow;
            const controls = [...(row?.querySelectorAll('a, button') || [])];
            const sameLink = element => element && element.getAttribute('href') === timelineFocus.href
                && (element.getAttribute('aria-label') || element.textContent) === timelineFocus.label;
            const control = timelineFocus.href !== null
                ? (sameLink(controls[timelineFocus.index]) ? controls[timelineFocus.index]
                    : controls.find(sameLink) || controls.find(element => element.getAttribute('href') === timelineFocus.href))
                : controls[timelineFocus.index];
            (active?.isConnected ? active : control)?.focus({ preventScroll: true });
        }

        const rowsNow = table.querySelectorAll(".row-appear:not(.is-visible)");
        if (!prefersReduced && rowsNow.length){
            const io = ensureRowObserver();
            rowsNow.forEach(r => io.observe(r));
            requestAnimationFrame(() => revealVisibleRowsImmediately(table));
        } else {
            rowsNow.forEach(r => r.classList.add("is-visible"));
        }
        tickRelativeLabels();

        const requestedEventId = getRequestedEventId();
        if (requestedEventId && window.CSS && typeof window.CSS.escape === "function") {
            const targetRow = table.querySelector(`tr.main-row[data-event-id="${window.CSS.escape(requestedEventId)}"]`);
            if (targetRow && !timelineRows.some(row => row.dataset.eventId === requestedEventId)) {
                if (targetRow.classList.contains("custom-event-row") && !targetRow.classList.contains("is-open")) {
                    targetRow._setCustomEventExpanded?.(true);
                }

                requestAnimationFrame(() => {
                    targetRow.scrollIntoView({ behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth', block: "center" });
                });
            }
        }
    }

    function normalizeBadgeKey(s){
        return String(s||"")
            .replace(/[–—]/g,"-")
            .replace(/\s+/g," ")
            .trim()
            .toLowerCase();
    }

    function isTop3Place(place){
        return place===1 || place===2 || place===3;
    }

    const HIGHLIGHT_BADGE_KEYS = new Set([
        "chronological age - oldest",
        "chronological age - youngest",
        "phenoage - lowest",
        "bortz age - lowest",
        "phenoage best improvement",
        "bortz age best improvement"
    ]);

    const VIEW_ALL_EXTRA_BADGE_KEYS = new Set([
        "pheno pace of aging",
        "bortz pace of aging"
    ]);

    function isAllowedBadgeHighlights(label, leagueCategory, place){
        const key = normalizeBadgeKey(label);
        if(key==="podcast") return true;
        if(key==="pregnancy") return true;

        if(key==="age reduction"){
            if(!isTop3Place(place)) return false;
            const cat = normalizeBadgeKey(leagueCategory);
            return cat==="global" || cat==="generation" || cat==="division" || cat==="exclusive" || cat==="amateur";
        }

        if(HIGHLIGHT_BADGE_KEYS.has(key)){
            return isTop3Place(place);
        }

        return false;
    }

    function isAllowedBadgeViewAll(label, leagueCategory, place){
        if(isAllowedBadgeHighlights(label, leagueCategory, place)) return true;

        const key = normalizeBadgeKey(label);
        if(VIEW_ALL_EXTRA_BADGE_KEYS.has(key)) return isTop3Place(place);
        if(key.startsWith("best domain -")) return isTop3Place(place);

        return false;
    }

    function isAllowedLongevitymaxxingChallengeResult(row, mode){
        if(mode === "viewAll") return true;
        return isTop3Place(row && row.place);
    }

    function filterBadgeRowsForMainFeed(rows, mode){
        const allowFn = mode==="viewAll" ? isAllowedBadgeViewAll : isAllowedBadgeHighlights;

        const out=[];
        for(const r of rows){
            if(!(r && r.type===EVENT_TYPE.BadgeAward)){
                out.push(r);
                continue;
            }

            if(r.isGroup && Array.isArray(r.items)){
                const total = Number.isFinite(r.badgeCount) ? r.badgeCount : r.items.length;

                const kept = r.items.filter(it => allowFn(it.label, it.leagueCategory, it.place));
                if(kept.length===0) continue;

                if(kept.length===1){
                    const it = kept[0];
                    out.push({
                        ...r,
                        isGroup:false,
                        badgeCount:null,
                        badges:null,
                        items:null,
                        badgeLabel: it.label || r.badgeLabel || "badge",
                        prevSlug: it.prevSlug || null,
                        otherSlug: it.otherSlug || null,
                        leagueCategory: it.leagueCategory || null,
                        leagueValue: it.leagueValue || null,
                        place: (it.place ?? null),
                        totalBadgeCount: total,
                        allItems: r.items
                    });
                    continue;
                }

                const uniqueLabels=[...new Set(kept.map(it=>it.label).filter(Boolean))];
                out.push({
                    ...r,
                    isGroup:true,
                    badgeCount: kept.length,
                    badges: uniqueLabels,
                    items: kept,
                    allItems: r.items,
                    totalBadgeCount: total
                });
                continue;
            }

            if(allowFn(r.badgeLabel, r.leagueCategory, r.place)){
                out.push(r);
            }
        }

        return out;
    }

    const HOMEPAGE_ATHLETE_HIGHLIGHT_TYPES = new Set([
        EVENT_TYPE.Joined,
        EVENT_TYPE.NewRank,
        EVENT_TYPE.BadgeAward,
        EVENT_TYPE.LongevitymaxxingChallengeResult,
        EVENT_TYPE.BecamePro,
        EVENT_TYPE.BiologicalAgeImproved,
        EVENT_TYPE.CrowdAgeTop10Change,
        EVENT_TYPE.AgeImprovementTop10Change
    ]);

    function rowTimeMs(row){
        const t=+new Date(row && row.occurredAt ? row.occurredAt : 0);
        return Number.isFinite(t) ? t : 0;
    }

    const HOMEPAGE_ATHLETE_FRESHNESS_WINDOW_MS = 14 * 24 * 60 * 60 * 1000;

    function topPlaceBonus(place, maxPlace=10){
        if(!Number.isFinite(place) || place<1 || place>maxPlace) return 0;
        return maxPlace + 1 - place;
    }

    function homepageAthleteHighlightKey(row){
        if(!(row && row.primarySlug && HOMEPAGE_ATHLETE_HIGHLIGHT_TYPES.has(row.type))) return null;
        return String(row.primarySlug);
    }

    function badgeHighlightScore(row){
        const key=normalizeBadgeKey(row && row.badgeLabel);
        const placeBonus=topPlaceBonus(row && row.place, 3);
        let score=placeBonus*35;

        if(row && row.isGroup){
            const count=Number.isFinite(row.badgeCount) ? row.badgeCount : (Array.isArray(row.items) ? row.items.length : 1);
            score += Math.min(count, 6)*12;
        }

        if(key==="age reduction") score += 120;
        else if(key==="pheno age - lowest" || key==="bortz age - lowest" || key==="crowd age - lowest") score += 110;
        else if(key==="pheno age best improvement" || key==="bortz age best improvement") score += 100;
        else if(key.startsWith("best domain -")) score += 85;
        else score += 55;

        const cat=normalizeBadgeKey(row && row.leagueCategory);
        if(!cat || cat==="global") score += 12;
        return score;
    }

    function homepageHighlightImportance(row){
        if(!row) return 0;

        const relevance=Number.isFinite(row.relevance) ? row.relevance*20 : 0;
        switch(row.type){
            case EVENT_TYPE.NewRank:
                return 10000 + relevance + topPlaceBonus(row.rank, 10)*120;
            case EVENT_TYPE.BecamePro:
                return 9400 + relevance;
            case EVENT_TYPE.BiologicalAgeImproved: {
                const delta = Number.isFinite(row.bioAgeFrom) && Number.isFinite(row.bioAgeTo)
                    ? Math.max(0, row.bioAgeFrom - row.bioAgeTo)
                    : 0;
                return 9300 + relevance + Math.min(delta, 20)*20;
            }
            case EVENT_TYPE.BadgeAward:
                return 9000 + relevance + badgeHighlightScore(row);
            case EVENT_TYPE.LongevitymaxxingChallengeResult:
                return 8800 + relevance + topPlaceBonus(row.place, 10)*50 + (row.challengeCompleted ? 25 : 0);
            case EVENT_TYPE.CrowdAgeTop10Change: {
                const movement = Number.isFinite(row.previousPlace) && Number.isFinite(row.place)
                    ? Math.max(0, row.previousPlace - row.place)
                    : 0;
                return 8700 + relevance + topPlaceBonus(row.place, 10)*45 + movement*10;
            }
            case EVENT_TYPE.AgeImprovementTop10Change: {
                const magnitude = Number.isFinite(row.eventImprovement) ? Math.min(Math.abs(row.eventImprovement), 25) : 0;
                const movement = Number.isFinite(row.previousPlace) && Number.isFinite(row.place)
                    ? Math.max(0, row.previousPlace - row.place)
                    : 0;
                return 9500 + relevance + topPlaceBonus(row.place, 10)*60 + movement*10 + magnitude*5;
            }
            case EVENT_TYPE.Joined:
                return 5000 + relevance;
            default:
                return relevance;
        }
    }

    function compareHomepageHighlightPreference(a,b){
        const ta=rowTimeMs(a);
        const tb=rowTimeMs(b);
        if(ta && tb && Math.abs(ta-tb)>HOMEPAGE_ATHLETE_FRESHNESS_WINDOW_MS) return ta-tb;

        const sa=homepageHighlightImportance(a);
        const sb=homepageHighlightImportance(b);
        if(sa!==sb) return sa-sb;

        if(ta!==tb) return ta-tb;

        const ia=String(a && a.id || "");
        const ib=String(b && b.id || "");
        return ia.localeCompare(ib);
    }

    function selectHomepageHighlightRows(rows, maxRows){
        if(!Number.isFinite(maxRows) || maxRows<=0) return rows;

        const nonAthlete=[];
        const bestByAthlete=new Map();
        const repeats=[];

        for(const row of rows){
            const key=homepageAthleteHighlightKey(row);
            if(!key){
                row.homepageHighlightSelection = "global";
                nonAthlete.push(row);
                continue;
            }

            const existing=bestByAthlete.get(key);
            if(!existing){
                row.homepageHighlightSelection = "primary";
                bestByAthlete.set(key,row);
                continue;
            }

            if(compareHomepageHighlightPreference(row,existing)>0){
                existing.homepageHighlightSelection = "repeat";
                repeats.push(existing);
                row.homepageHighlightSelection = "primary";
                bestByAthlete.set(key,row);
            }else{
                row.homepageHighlightSelection = "repeat";
                repeats.push(row);
            }
        }

        const uniqueRows=nonAthlete.concat([...bestByAthlete.values()]);
        if(uniqueRows.length>=maxRows) return uniqueRows;

        repeats.sort((a,b)=>compareHomepageHighlightPreference(b,a));
        const backfill = repeats.slice(0,maxRows-uniqueRows.length);
        backfill.forEach(row => {
            row.homepageHighlightSelection = "backfill-repeat";
        });
        return uniqueRows.concat(backfill);
    }

    function normalizeIncomingAthleteId(id){
        if(id==null) return '';
        let s=String(id).trim();
        if(!s) return '';
        s=s.split('#')[0].split('?')[0];
        if(s.includes('/')) s=s.split('/').filter(Boolean).pop();
        return normalizeLinkId(slugifyLocal(s));
    }

    window.loadEventsTable=function(maxRows=null,showViewAll=true,athleteId=null,linkNames=true,options=null){
        const feed = window.document.querySelector('main.event-board-page');
        if (feed && !feed.hasAttribute('data-lwc-refresh-ready')) {
            feed.setAttribute('data-lwc-refresh-ready', '');
            feed.addEventListener('lwc:pull-refresh', event => {
                const reload = () => window.loadEventsTable(maxRows, showViewAll, athleteId, linkNames,
                    { ...options, live: true, preserveExistingRows: true });
                event.detail.waitUntil(typeof window.callScoped === 'function'
                    ? window.callScoped(loadRoot.parentElement, reload) : reload());
            });
            window.LwcTouch?.refresh();
        }
        const requestedPreserveExistingRows = !!(options && options.preserveExistingRows);
        const live = !!(options && options.live);
        const useSharedEvents = !!(options && options.useSharedEvents) && !live;
        const loadRoot = document.getElementById('events-root');
        if (!loadRoot) return Promise.resolve();
        const request = {};
        feedRequests.set(loadRoot, request);
        const current = () => loadRoot.isConnected && feedRequests.get(loadRoot) === request;
        const scoped = callback => typeof window.callScoped === 'function' ? window.callScoped(loadRoot.parentElement, callback) : callback();
        const loadKey = loadRoot.parentElement?.id === 'events-root-index' ? 'events-root-index' : 'events-root';
        if (!live) setEventsLoadingState(requestedPreserveExistingRows || !!loadRoot.querySelector('tr.main-row'));
        setViewAllVisible(!!showViewAll);

        const n=(function(val){ if(val===undefined||val===null||val==="")return null; const nn=typeof val==="string"?parseInt(val,10):Number(val); return Number.isFinite(nn)&&nn>0?Math.floor(nn):null; })(maxRows);

        const eventsReq=window.getSharedEvents(!useSharedEvents);
        const athletesReq=window.getSharedAthletes(live);

        return Promise.all([eventsReq,athletesReq]).then(([events,athletes])=>scoped(()=>{
            if (!current()) return;
            const athletesIndex=buildAthleteIndex(athletes);
            feedAthletesIndex=athletesIndex;
            eventBoardMarkupOptions = {
                mentionResolver: slug => resolveMentionName(slug, athletesIndex),
                mentionHrefResolver: slug => {
                    const key = slugifyLocal(normalizeLinkId(slug));
                    return athletesIndex.has(key) ? makeAthleteUrlFromSlug(slug) : "";
                }
            };
            window.CustomEventMarkup.setMentionResolver(slug => resolveMentionName(slug, athletesIndex));
            let rows=toRows(Array.isArray(events)?events:[]);

            const targetArg=normalizeIncomingAthleteId(athleteId);
            const root=document.getElementById('events-root');
            const rootContainerId = root && root.parentElement ? root.parentElement.id : "";
            const canInferAthleteFromLocation = rootContainerId !== "events-root-index";

            const targetPath=(function(){
                if(targetArg || !canInferAthleteFromLocation) return "";
                const p=String(window.location.pathname||"");
                if(!/\/athlete\//i.test(p)) return "";
                return normalizeIncomingAthleteId(p);
            })();

            const targetParentPath=(function(){
                if(targetArg || targetPath || !canInferAthleteFromLocation) return "";
                try{
                    if(window===window.parent) return "";
                    const pp=String(window.parent.location && window.parent.location.pathname || "");
                    if(!/\/athlete\//i.test(pp)) return "";
                    return normalizeIncomingAthleteId(pp);
                }catch(_){
                    return "";
                }
            })();

            const target=targetArg || targetPath || targetParentPath;

            allowBadgeClicks = !target;

            if(root) root.classList.toggle('athlete-scroll', !!target);

            athletePageTargetSlug = target || null;
            openLinksInNewTab = !!target;

            const mode = target ? "athlete" : (n!==null ? "highlights" : "viewAll");

            if(target){
                rows=rows.filter(r=>{
                    if(r.type===EVENT_TYPE.AthleteCountMilestone) return false;
                    return r.slugs.includes(target);
                });

                rows.forEach(r=>{
                    if(r.type===EVENT_TYPE.BadgeAward){
                        if(r.primarySlug===target){
                            if(r.isSolo){
                                r.badgeDirection = "solo";
                                r.badgeBurstKey = `${target}|solo|${r.badgeLabel||""}|${r.leagueCategory||""}|${r.leagueValue||""}|${r.place ?? ""}`;
                            }else{
                                r.badgeDirection = "gained";
                                r.badgeBurstKey = `${target}|gained`;
                            }
                            r.otherSlug = null;
                            return;
                        }

                        if(r.prevSlug===target || (Array.isArray(r.prevSlugs) && r.prevSlugs.includes(target))){
                            const winner = r.primarySlug;
                            r.badgeDirection = "lost";
                            r.badgeBurstKey = `${target}|lost`;
                            r.otherSlug = winner || null;

                            r.primarySlug = target;
                            r.prevSlug = null;
                            return;
                        }
                    }

                    if(r.type===EVENT_TYPE.NewRank){
                        if(r.prevSlug===target){
                            const winner = r.primarySlug;

                            r.newRankDirection = "lost";
                            r.otherSlug = winner || null;

                            r.primarySlug = target;
                            r.prevSlug = null;
                            return;
                        }
                    }

                    if(r.type===EVENT_TYPE.CrowdAgeTop10Change){
                        if(r.prevSlug===target){
                            const winner = r.primarySlug;

                            r.crowdAgeDirection = "lost";
                            r.otherSlug = winner || null;

                            r.primarySlug = target;
                            r.prevSlug = null;
                            return;
                        }
                    }

                    if(r.type===EVENT_TYPE.AgeImprovementTop10Change){
                        if(r.prevSlug===target){
                            const winner = r.primarySlug;

                            r.improvementDirection = "lost";
                            r.otherSlug = winner || null;

                            r.primarySlug = target;
                            r.prevSlug = null;
                            return;
                        }
                    }
                });
            }else{
                rows=rows.filter(r=>
                    r.type!==EVENT_TYPE.SeasonFinalResult &&
                    r.type!==EVENT_TYPE.TestResultAccepted &&
                    (r.type!==EVENT_TYPE.LongevitymaxxingChallengeResult || isAllowedLongevitymaxxingChallengeResult(r, mode)) &&
                    (r.type!==EVENT_TYPE.NewRank || isTop10Rank(r, athletesIndex)));
            }

            rows = applyArrivalOrderEpsilon(rows, 1);
            rows = groupBadgeBursts(rows, 60000);

            if(!target){
                rows = filterBadgeRowsForMainFeed(rows, mode);
            }

            if(mode==="highlights"){
                rows = selectHomepageHighlightRows(rows, n);
            }

            rows.sort((a,b)=>{
                const ta=+new Date(a.displayOccurredAt||a.occurredAt||0);
                const tb=+new Date(b.displayOccurredAt||b.occurredAt||0);
                if(!Number.isFinite(ta) && !Number.isFinite(tb)) return 0;
                if(!Number.isFinite(ta)) return -1;
                if(!Number.isFinite(tb)) return 1;
                return tb - ta || +new Date(b.occurredAt||0) - +new Date(a.occurredAt||0);
            });

            rows = groupAthleteBursts(rows, 60000, mode === "viewAll" && !!root?.closest('.event-board-page'));

            if(n!==null) rows=rows.slice(0,n);
            const currentRowIds = rows.map(r => r && r.id ? String(r.id) : '').filter(Boolean);
            renderRows(rows,athletesIndex,!!linkNames,{
                seenMode: (!live && mode !== "athlete") ? mode : null,
                layoutOnly: !!options?.layoutOnly && !live
            });
            if (mode !== "athlete") {
                feedLoads.set(loadKey, [maxRows, showViewAll, athleteId, linkNames, options && { ...options, live: false }]);
                if (live) writeSeenAt(mode, rows.reduce((max, r) => Math.max(max, +new Date(r.occurredAt || 0) || 0), 0));
            }
            if(mode === "highlights"){
                window.__homepageLastRenderIds = currentRowIds;
            }
            clearEventsLoadingState(rows.length ? 'Events loaded.' : 'No events yet.');
        })).catch(()=>scoped(()=>{
            if (!current()) return;
            // A missed background refresh leaves the feed as it is.
            if (live) return;
            clearEventsLoadingState('Events could not load.');
            const status = document.getElementById('eventsStatus');
            if (status) status.setAttribute('role', 'alert');
            renderEventsLoadError(() => {
                window.loadEventsTable(maxRows, showViewAll, athleteId, linkNames, options);
            });
        }));
    };

})();
