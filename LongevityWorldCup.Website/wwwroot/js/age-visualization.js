/**
 * Age Visualization Module
 * Radar chart of domain percentiles (Pheno Age or Bortz) with age reduction in the center.
 */

(function () {
    'use strict';

    // Pheno Age: 5 domains (Immune first for radar; aligned with Best Domain badges)
    var PHENO_DOMAINS = [
        { key: 'immune', label: 'Immune', contributor: 'calculateImmunePhenoAgeContributor' },
        { key: 'liver', label: 'Liver', contributor: 'calculateLiverPhenoAgeContributor' },
        { key: 'kidney', label: 'Kidney', contributor: 'calculateKidneyPhenoAgeContributor' },
        { key: 'metabolic', label: 'Metabolism', contributor: 'calculateMetabolicPhenoAgeContributor' },
        { key: 'inflammation', label: 'Inflammation', contributor: 'calculateInflammationPhenoAgeContributor' }
    ];
    // Pheno biomarker index -> { shortName, unit, fromStored? } for tooltips. Stored order: age, albumin, creatinine, glucose, log(crp/10), wbc, lymphocyte, mcv, rcdw, ap
    var PHENO_BIOMARKER_DISPLAY = {
        1: { short: 'Albumin', unit: 'g/L' },
        2: { short: 'Creatinine', unit: 'µmol/L' },
        3: { short: 'Glucose', unit: 'mmol/L' },
        4: { short: 'CRP', unit: 'mg/L', fromStored: function (v) { return 10 * Math.exp(v); } },
        5: { short: 'WBC', unit: '10⁹/L' },
        6: { short: 'Lymphocytes', unit: '%' },
        7: { short: 'MCV', unit: 'fL' },
        8: { short: 'RDW', unit: '%' },
        9: { short: 'ALP', unit: 'U/L' }
    };
    var PHENO_DOMAIN_BIOMARKER_INDICES = {
        immune: [5, 6, 7, 8],
        liver: [1, 9],
        kidney: [2],
        metabolic: [3],
        inflammation: [4]
    };

    // Bortz: 6 domains used for athlete profile radar and Best Domain badges (aligned with BadgeDataService.cs).
    // Feature indices in window.BortzAge.features: 0=age, 1=albumin, 2=alp, 3=urea, 4=cholesterol, 5=creatinine, 6=cystatin_c, 7=hba1c, 8=crp, 9=ggt, 10=rbc, 11=mcv, 12=rdw, 13=monocyte, 14=neutrophil, 15=lymphocyte, 16=alt, 17=shbg, 18=vitamin_d, 19=glucose, 20=mch, 21=apoa1
    // Excluded from contribution only (controversial direction): urea, cholesterol, creatinine, alt, shbg
    var BORTZ_CONTRIBUTION_EXCLUDED = { 3: 1, 4: 1, 5: 1, 16: 1, 17: 1 };
    var BORTZ_DOMAIN_INDICES = {
        Immune: [15, 14, 13, 10, 11, 20, 12],
        Liver: [1, 16, 2, 9],
        Kidney: [3, 5, 6],
        Metabolism: [19, 7, 4, 21],
        Inflammation: [8],
        'Vitamin D': [17, 18]
    };
    var BORTZ_DOMAIN_LABELS = ['Immune', 'Liver', 'Kidney', 'Metabolism', 'Inflammation', 'Vitamin D'];
    // Bortz feature index -> unit for tooltips (indices match window.BortzAge.features)
    var BORTZ_BIOMARKER_UNITS = {
        0: 'years', 1: 'g/L', 2: 'U/L', 3: 'mmol/L', 4: 'mmol/L', 5: 'µmol/L', 6: 'mg/L', 7: 'mmol/mol',
        8: 'mg/L', 9: 'U/L', 10: '10¹²/L', 11: 'fL', 12: '%', 13: '10⁹/L', 14: '10⁹/L', 15: '%',
        16: 'U/L', 17: 'nmol/L', 18: 'nmol/L', 19: 'mmol/L', 20: 'pg', 21: 'g/L'
    };

    function applyBortzCap(value, f) {
        if (!f.capMode) return value;
        if (f.capMode === 'floor') return Math.max(value, f.cap);
        if (f.capMode === 'ceiling') return Math.min(value, f.cap);
        return value;
    }

    /** Bortz domain contribution (sum of (x-mean)*coeff for indices in that domain, excluding controversial biomarkers). Lower = better. */
    function getBortzDomainContribution(values, featureIndices) {
        if (!window.BortzAge || !window.BortzAge.features || !values || values.length !== window.BortzAge.features.length)
            return NaN;
        var sum = 0;
        for (var i = 0; i < featureIndices.length; i++) {
            var idx = featureIndices[i];
            if (BORTZ_CONTRIBUTION_EXCLUDED[idx]) continue;
            var f = window.BortzAge.features[idx];
            var x = values[idx];
            if (f.isLog) {
                if (x <= 0) return NaN;
                x = Math.log(x);
            }
            x = applyBortzCap(x, f);
            sum += (x - f.mean) * f.baaCoeff;
        }
        return sum * 10;
    }

    function formatTooltipValue(val) {
        if (val !== val || val === undefined) return '—';
        if (Number.isInteger(val)) return String(val);
        return Number(val).toFixed(2).replace(/\.?0+$/, '');
    }

    /** Capitalize biomarker id for tooltip (e.g. "glucose" -> "Glucose", "vitamin_d" -> "Vitamin D"). */
    function formatBortzLabel(id) {
        if (!id) return '—';
        return id.split('_').map(function (part) { return part.charAt(0).toUpperCase() + part.slice(1).toLowerCase(); }).join(' ');
    }

    /** For a list of athletes with scores (lower = better), compute percentile for the current athlete. 0–100, higher = better. */
    function scoreToPercentile(currentScore, allScores) {
        if (allScores.length === 0 || currentScore === undefined || currentScore !== currentScore) return null;
        var sorted = allScores.slice().filter(function (s) { return s === s; }).sort(function (a, b) { return a - b; });
        var n = sorted.length;
        if (n === 0) return null;
        var rank = 1;
        for (var i = 0; i < sorted.length; i++) {
            if (sorted[i] < currentScore) rank++;
            else break;
        }
        return Math.round((n - rank + 1) / n * 100);
    }

    /** Returns up to 100 athletes closest by chronological age (absolute difference). */
    function getClosestAthletesByAge(currentChronoAge, athletes, chronoKey) {
        if (!Number.isFinite(currentChronoAge) || !Array.isArray(athletes)) return [];

        return athletes
            .filter(function (a) {
                return a && Number.isFinite(a[chronoKey]);
            })
            .sort(function (a, b) {
                return Math.abs(a[chronoKey] - currentChronoAge) - Math.abs(b[chronoKey] - currentChronoAge);
            })
            .slice(0, 100);
    }


    function getPhenoRadarData(athleteData, athleteResults, currentChronoAge) {
        var labels = [];
        var values = [];
        var tooltipContributors = [];
        var athletesWithPheno = getClosestAthletesByAge(
            currentChronoAge,
            (athleteResults || []).filter(function (a) { return a.bestBiomarkerValues && a.bestBiomarkerValues.length; }),
            'chronoAtLowestPhenoAge'
        );
        if (athletesWithPheno.length === 0 || !athleteData || !athleteData.bestBiomarkerValues) return null;

        var mv = athleteData.bestBiomarkerValues;
        for (var d = 0; d < PHENO_DOMAINS.length; d++) {
            var dom = PHENO_DOMAINS[d];
            labels.push(dom.label);
            var contributorFn = window.PhenoAge && window.PhenoAge[dom.contributor];
            if (typeof contributorFn !== 'function') {
                values.push(50);
                tooltipContributors.push([]);
                continue;
            }
            var myScore = contributorFn(mv);
            var allScores = athletesWithPheno.map(function (a) { return contributorFn(a.bestBiomarkerValues); });
            var pct = scoreToPercentile(myScore, allScores);
            values.push(pct !== null ? pct : 50);
            var indices = PHENO_DOMAIN_BIOMARKER_INDICES[dom.key];
            var parts = [];
            if (indices) {
                for (var i = 0; i < indices.length; i++) {
                    var idx = indices[i];
                    var disp = PHENO_BIOMARKER_DISPLAY[idx];
                    var v = mv[idx];
                    if (disp) {
                        var displayVal = disp.fromStored ? disp.fromStored(v) : v;
                        parts.push(disp.short + ': ' + formatTooltipValue(displayVal) + ' ' + disp.unit);
                    }
                }
            }
            tooltipContributors.push(parts);
        }
        return { labels: labels, values: values, tooltipContributors: tooltipContributors };
    }

    function getBortzRadarData(athleteData, athleteResults, currentChronoAge) {
        var labels = BORTZ_DOMAIN_LABELS.slice();
        var values = [];
        var tooltipContributors = [];
        var athletesWithBortz = getClosestAthletesByAge(
            currentChronoAge,
            (athleteResults || []).filter(function (a) { return a.bestBortzValues && a.bestBortzValues.length; }),
            'chronoAtLowestBortzAge'
        );
        if (athletesWithBortz.length === 0 || !athleteData || !athleteData.bestBortzValues) return null;

        var bv = athleteData.bestBortzValues;
        var features = window.BortzAge && window.BortzAge.features;
        for (var i = 0; i < BORTZ_DOMAIN_LABELS.length; i++) {
            var name = BORTZ_DOMAIN_LABELS[i];
            var indices = BORTZ_DOMAIN_INDICES[name];
            var myScore = getBortzDomainContribution(bv, indices);
            var allScores = athletesWithBortz.map(function (a) { return getBortzDomainContribution(a.bestBortzValues, indices); });
            var pct = scoreToPercentile(myScore, allScores);
            values.push(pct !== null ? pct : 50);
            var parts = [];
            if (indices && features) {
                for (var j = 0; j < indices.length; j++) {
                    var idx = indices[j];
                    if (BORTZ_CONTRIBUTION_EXCLUDED[idx]) continue;
                    var f = features[idx];
                    var v = bv[idx];
                    var shortName = f && f.id ? (f.id === 'alp' ? 'ALP' : f.id === 'crp' ? 'CRP' : f.id === 'hba1c' ? 'HbA1c' : f.id === 'ggt' ? 'GGT' : f.id === 'rbc' ? 'RBC' : f.id === 'mcv' ? 'MCV' : f.id === 'mch' ? 'MCH' : f.id === 'rdw' ? 'RDW' : f.id === 'alt' ? 'ALT' : f.id === 'apoa1' ? 'ApoA1' : f.id === 'monocyte_percentage' ? 'Monocytes' : f.id === 'neutrophil_percentage' ? 'Neutrophils' : f.id === 'lymphocyte_percentage' ? 'Lymphocytes' : f.id === 'cystatin_c' ? 'Cystatin C' : f.id === 'vitamin_d' ? 'Vitamin D' : f.id === 'shbg' ? 'SHBG' : formatBortzLabel(f.id)) : '—';
                    var unit = BORTZ_BIOMARKER_UNITS[idx] || '';
                    parts.push(shortName + ': ' + formatTooltipValue(v) + (unit ? ' ' + unit : ''));
                }
            }
            tooltipContributors.push(parts);
        }
        return { labels: labels, values: values, tooltipContributors: tooltipContributors };
    }

    var radarChartInstance = null;
    var radarResizeObserver = null;

    function destroyRadarChart() {
        if (radarResizeObserver) {
            var wrapper = document.getElementById('ageRadarWrapper');
            if (wrapper) radarResizeObserver.disconnect();
            radarResizeObserver = null;
        }
        if (radarChartInstance) {
            radarChartInstance.destroy();
            radarChartInstance = null;
        }
    }

    function updateCenterAndFallback(bioAge, chronoAge, useBortz) {
        var diff = bioAge - chronoAge;
        var diffText = (diff > 0 ? '+' : '') + diff.toFixed(1) + ' years';
        var label = diff < 0 ? 'age reduction' : 'age acceleration';
        var bioText = 'biological age: ' + (Number.isFinite(bioAge) ? bioAge.toFixed(1) : '—');

        var centerVal = document.getElementById('ageRadarCenterValue');
        var centerLbl = document.getElementById('ageRadarCenterLabel');
        var centerBio = document.getElementById('ageRadarCenterBio');
        var centerEl = document.getElementById('ageRadarCenter');
        var fallbackVal = document.getElementById('ageRadarFallbackValue');
        var fallbackLbl = document.getElementById('ageRadarFallbackLabel');
        var fallbackBio = document.getElementById('ageRadarFallbackBio');
        if (centerVal) centerVal.textContent = diffText;
        if (centerLbl) centerLbl.textContent = label;
        if (centerBio) centerBio.textContent = bioText;
        if (centerEl) {
            centerEl.classList.remove('positive', 'negative');
            if (diff < 0) centerEl.classList.add('positive');
            else if (diff > 0) centerEl.classList.add('negative');
            centerEl.setAttribute('data-diff-sign', diff < 0 ? 'negative' : diff > 0 ? 'positive' : '');
        }
        if (fallbackVal) fallbackVal.textContent = diffText;
        if (fallbackLbl) fallbackLbl.textContent = label;
        if (fallbackBio) fallbackBio.textContent = bioText;
    }

    function applyRadarData(data) {
        if (!radarChartInstance || !data) return;
        radarChartInstance.data.labels = data.labels;
        radarChartInstance.data.datasets[0].data = data.values;
        if (data.tooltipContributors) radarChartInstance._radarTooltipContributors = data.tooltipContributors;
        radarChartInstance.update('active');
    }

    var currentClockState = { hasPheno: false, hasBortz: false, pheno: null, bortz: null };

    function generateAgeVisualization(bioAge, chronoAge, athleteData, athleteResults) {
        var wrapper = document.getElementById('ageRadarWrapper');
        var fallback = document.getElementById('ageRadarFallback');
        var canvas = document.getElementById('ageRadarChart');
        var switchEl = document.getElementById('ageClockSwitch');
        var tabPheno = document.getElementById('ageTabPheno');
        var tabBortz = document.getElementById('ageTabBortz');

        if (!wrapper || !fallback) {
            var legacy = document.getElementById('targetShootingVisualization') || document.querySelector('#ageVisualization #targetShootingVisualization');
            if (legacy) {
                generateAgeVisualizationInternal(legacy, bioAge, chronoAge);
            }
            return;
        }

        var hasPheno = athleteData && athleteData.bestBiomarkerValues && athleteData.bestBiomarkerValues.length && (athleteResults || []).some(function (a) { return a.bestBiomarkerValues && a.bestBiomarkerValues.length; });
        var hasBortz = athleteData && athleteData.bestBortzValues && athleteData.bestBortzValues.length && (athleteResults || []).some(function (a) { return a.bestBortzValues && a.bestBortzValues.length; });
        var bothClocks = hasPheno && hasBortz;

        if (switchEl) {
            if (bothClocks) {
                switchEl.classList.add('visible');
                if (tabBortz) {
                    tabBortz.setAttribute('aria-selected', 'true');
                    tabBortz.tabIndex = 0;
                }
                if (tabPheno) {
                    tabPheno.setAttribute('aria-selected', 'false');
                    tabPheno.tabIndex = -1;
                }
            } else {
                switchEl.classList.remove('visible');
            }
        }

        var phenoData = hasPheno ? getPhenoRadarData(athleteData, athleteResults, athleteData.chronoAtLowestPhenoAge) : null;
        var bortzData = hasBortz ? getBortzRadarData(athleteData, athleteResults, athleteData.chronoAtLowestBortzAge) : null;
        var phenoBio = athleteData && Number.isFinite(athleteData.lowestPhenoAge) ? athleteData.lowestPhenoAge : null;
        var phenoChrono = athleteData && Number.isFinite(athleteData.chronoAtLowestPhenoAge) ? athleteData.chronoAtLowestPhenoAge : null;
        var bortzBio = athleteData && Number.isFinite(athleteData.lowestBortzAge) ? athleteData.lowestBortzAge : null;
        var bortzChrono = athleteData && Number.isFinite(athleteData.chronoAtLowestBortzAge) ? athleteData.chronoAtLowestBortzAge : null;

        currentClockState = {
            hasPheno: hasPheno,
            hasBortz: hasBortz,
            pheno: (phenoData && phenoBio != null && phenoChrono != null) ? { data: phenoData, bio: phenoBio, chrono: phenoChrono } : null,
            bortz: (bortzData && bortzBio != null && bortzChrono != null) ? { data: bortzData, bio: bortzBio, chrono: bortzChrono } : null
        };

        var useBortz = hasBortz && !hasPheno ? true : (bothClocks ? true : !!hasBortz);
        var data = useBortz ? (bortzData || phenoData) : (phenoData || bortzData);
        var showBio = useBortz ? bortzBio : phenoBio;
        var showChrono = useBortz ? bortzChrono : phenoChrono;
        if (showBio == null) showBio = bioAge;
        if (showChrono == null) showChrono = chronoAge;
        updateCenterAndFallback(showBio, showChrono, useBortz);

        var centerEl = document.getElementById('ageRadarCenter');
        if (!data || !window.Chart || !canvas) {
            wrapper.style.display = 'none';
            if (centerEl) centerEl.style.display = 'none';
            fallback.style.display = 'flex';
            fallback.style.flexDirection = 'column';
            fallback.style.alignItems = 'center';
            fallback.style.justifyContent = 'center';
            return;
        }

        wrapper.style.display = 'block';
        if (centerEl) centerEl.style.display = 'flex';
        fallback.style.display = 'none';

        destroyRadarChart();
        var ctx = canvas.getContext('2d');
        var fontFamily = window.getComputedStyle(document.body).fontFamily || 'system-ui, sans-serif';
        var primaryRgb = '0, 188, 212';
        var secondaryRgb = '255, 64, 129';
        var contributors = data.tooltipContributors || [];

        radarChartInstance = new window.Chart(ctx, {
            type: 'radar',
            data: {
                labels: data.labels,
                datasets: [{
                    label: 'Percentile (higher = better)',
                    data: data.values,
                    backgroundColor: 'rgba(' + primaryRgb + ', 0.18)',
                    borderColor: 'rgba(' + primaryRgb + ', 0.85)',
                    borderWidth: 2.5,
                    pointBackgroundColor: 'rgba(' + secondaryRgb + ', 0.9)',
                    pointBorderColor: '#fff',
                    pointBorderWidth: 2,
                    pointHoverBackgroundColor: 'rgba(' + secondaryRgb + ', 1)',
                    pointHoverBorderColor: '#fff',
                    pointHoverBorderWidth: 2,
                    pointRadius: 5,
                    pointHoverRadius: 7
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: true,
                layout: { padding: 4 },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        backgroundColor: 'rgba(0,0,0,0.78)',
                        padding: 10,
                        cornerRadius: 8,
                        titleFont: { size: 12, weight: '600' },
                        bodyFont: { size: 12 },
                        callbacks: {
                            title: function (tooltipItems) {
                                if (!tooltipItems.length) return '';
                                return tooltipItems[0].raw + 'th percentile';
                            },
                            label: function () { return null; },
                            afterBody: function (tooltipItems) {
                                if (!tooltipItems.length) return [];
                                var chart = radarChartInstance;
                                if (!chart) return [];
                                var contrib = chart._radarTooltipContributors;
                                var idx = tooltipItems[0].dataIndex;
                                if (!contrib || !contrib[idx] || !contrib[idx].length) return [];
                                return [''].concat(contrib[idx]);
                            }
                        }
                    }
                },
                scales: {
                    r: {
                        min: 0,
                        max: 100,
                        beginAtZero: true,
                        ticks: {
                            stepSize: 25,
                            backdropColor: 'transparent',
                            display: false
                        },
                        pointLabels: {
                            font: { family: fontFamily, size: 11, weight: '500' },
                            color: 'rgba(0,0,0,0.7)',
                            padding: 3,
                            z: 1
                        },
                        grid: { color: 'rgba(0,0,0,0.08)', lineWidth: 1 },
                        angleLines: { color: 'rgba(0,0,0,0.08)', lineWidth: 1 }
                    }
                },
                animation: { duration: 500 },
                hover: { mode: 'nearest' }
            }
        });
        radarChartInstance._radarTooltipContributors = contributors;
        if (radarResizeObserver && wrapper) radarResizeObserver.disconnect();
        radarResizeObserver = new ResizeObserver(function () {
            if (radarChartInstance) radarChartInstance.resize();
        });
        radarResizeObserver.observe(wrapper);

        function selectClock(bortz) {
            var payload = bortz ? currentClockState.bortz : currentClockState.pheno;
            if (!payload) return;
            applyRadarData(payload.data);
            updateCenterAndFallback(payload.bio, payload.chrono, bortz);
            if (tabPheno) {
                tabPheno.setAttribute('aria-selected', bortz ? 'false' : 'true');
                tabPheno.tabIndex = bortz ? -1 : 0;
            }
            if (tabBortz) {
                tabBortz.setAttribute('aria-selected', bortz ? 'true' : 'false');
                tabBortz.tabIndex = bortz ? 0 : -1;
            }
        }

        if (bothClocks && tabPheno && tabBortz) {
            tabPheno.onclick = function () { selectClock(false); };
            tabBortz.onclick = function () { selectClock(true); };
            function handleKey(e, isPheno) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    selectClock(!isPheno);
                } else if (e.key === 'ArrowLeft') {
                    e.preventDefault();
                    selectClock(true);
                } else if (e.key === 'ArrowRight') {
                    e.preventDefault();
                    selectClock(false);
                }
            }
            tabPheno.onkeydown = function (e) { handleKey(e, true); };
            tabBortz.onkeydown = function (e) { handleKey(e, false); };
        }
    }

    function generateAgeVisualizationInternal(visualizationContainer, bioAge, chronoAge) {
        var maxAge = 100;
        var bioPct = Math.min((bioAge / maxAge) * 100, 100);
        var chronoPct = Math.min((chronoAge / maxAge) * 100, 100);
        var gradient;
        if (bioAge === chronoAge) {
            gradient = 'radial-gradient(circle, #3498db 100%, #3498db 100%)';
        } else if (bioAge < chronoAge) {
            gradient = 'radial-gradient(circle, #3498db ' + bioPct + '%, #2ecc71 ' + chronoPct + '%, #ccc 100%)';
        } else {
            gradient = 'radial-gradient(circle, #3498db ' + chronoPct + '%, #e74c3c ' + bioPct + '%, #ccc 100%)';
        }
        visualizationContainer.style.background = gradient;
        visualizationContainer.style.borderRadius = '50%';
        visualizationContainer.style.position = 'relative';
        visualizationContainer.style.border = '2px solid #ccc';
        visualizationContainer.innerHTML = '';
        var diff = (bioAge - chronoAge).toFixed(1);
        var text = (diff > 0 ? '+' : '') + diff + ' yrs';
        visualizationContainer.innerHTML = '<span style="position:absolute;top:50%;left:50%;transform:translate(-50%,-50%);color:var(--light-text-color);font-weight:bold;">' + text + '</span>';
    }

    /** Badge label -> Bortz domain key (same as athlete profile radar). Used so Best Domain badge tooltips show the same biomarkers as the profile. */
    var BEST_DOMAIN_LABEL_TO_KEY = {
        'Best Domain – Liver': 'Liver',
        'Best Domain – Kidney': 'Kidney',
        'Best Domain – Metabolic': 'Metabolism',
        'Best Domain – Immune': 'Immune',
        'Best Domain – Inflammation': 'Inflammation',
        'Best Domain – Vitamin D': 'Vitamin D'
    };

    /** Returns the biomarker part of a Best Domain tooltip, e.g. "(albumin 45 g/L, ALP 82 U/L, GGT 3.2 U/L)". Uses same indices and exclusions as athlete profile radar. */
    function getBestDomainBiomarkerTooltip(badgeLabel, bestBortzValues) {
        if (!bestBortzValues || bestBortzValues.length < 22) return null;
        var key = BEST_DOMAIN_LABEL_TO_KEY[badgeLabel];
        if (!key || !BORTZ_DOMAIN_INDICES[key]) return null;
        var indices = BORTZ_DOMAIN_INDICES[key];
        var features = window.BortzAge && window.BortzAge.features;
        var parts = [];
        for (var j = 0; j < indices.length; j++) {
            var idx = indices[j];
            if (BORTZ_CONTRIBUTION_EXCLUDED[idx]) continue;
            var f = features && features[idx];
            var v = bestBortzValues[idx];
            var shortName = f && f.id ? (f.id === 'alp' ? 'ALP' : f.id === 'crp' ? 'CRP' : f.id === 'hba1c' ? 'HbA1c' : f.id === 'ggt' ? 'GGT' : f.id === 'rbc' ? 'RBC' : f.id === 'mcv' ? 'MCV' : f.id === 'mch' ? 'MCH' : f.id === 'rdw' ? 'RDW' : f.id === 'alt' ? 'ALT' : f.id === 'apoa1' ? 'ApoA1' : f.id === 'monocyte_percentage' ? 'Monocytes' : f.id === 'neutrophil_percentage' ? 'Neutrophils' : f.id === 'lymphocyte_percentage' ? 'Lymphocytes' : f.id === 'cystatin_c' ? 'Cystatin C' : f.id === 'vitamin_d' ? 'Vitamin D' : f.id === 'shbg' ? 'SHBG' : formatBortzLabel(f.id)) : '—';
            var unit = BORTZ_BIOMARKER_UNITS[idx] || '';
            parts.push(shortName + ' ' + formatTooltipValue(v) + (unit ? ' ' + unit : ''));
        }
        return parts.length ? '(' + parts.join(', ') + ')' : null;
    }

    window.generateAgeVisualization = generateAgeVisualization;
    window.generateAgeVisualizationInternal = generateAgeVisualizationInternal;
    window.destroyAgeRadarChart = destroyRadarChart;
    window.getBestDomainBiomarkerTooltip = getBestDomainBiomarkerTooltip;
})();
