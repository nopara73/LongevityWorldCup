/**
 * Age Visualization Module
 * Radar chart of domain percentiles (Pheno Age or Bortz) with age reduction in the center.
 */

(function () {
    'use strict';

    // Pheno Age: 5 domains (aligned with Best Domain badges)
    var PHENO_DOMAINS = [
        { key: 'liver', label: 'Liver', contributor: 'calculateLiverPhenoAgeContributor' },
        { key: 'kidney', label: 'Kidney', contributor: 'calculateKidneyPhenoAgeContributor' },
        { key: 'metabolic', label: 'Metabolic', contributor: 'calculateMetabolicPhenoAgeContributor' },
        { key: 'inflammation', label: 'Inflammation', contributor: 'calculateInflammationPhenoAgeContributor' },
        { key: 'immune', label: 'Immune', contributor: 'calculateImmunePhenoAgeContributor' }
    ];

    // Bortz: 6 divisions from bortz-age.html (Immune, Liver, Kidney, Metabolism, Inflammation, Hormones)
    // Feature indices in window.BortzAge.features: 0=age, 1=albumin, 2=alp, 3=urea, 4=cholesterol, 5=creatinine, 6=cystatin_c, 7=hba1c, 8=crp, 9=ggt, 10=rbc, 11=mcv, 12=rdw, 13=monocyte, 14=neutrophil, 15=lymphocyte, 16=alt, 17=shbg, 18=vitamin_d, 19=glucose, 20=mch, 21=apoa1
    var BORTZ_DOMAIN_INDICES = {
        Immune: [10, 11, 12, 13, 14, 15, 20],
        Liver: [1, 2, 9, 16],
        Kidney: [3, 5, 6],
        Metabolism: [4, 7, 19, 21],
        Inflammation: [8],
        Hormones: [17, 18]
    };
    var BORTZ_DOMAIN_LABELS = ['Immune', 'Liver', 'Kidney', 'Metabolism', 'Inflammation', 'Hormones'];

    function applyBortzCap(value, f) {
        if (!f.capMode) return value;
        if (f.capMode === 'floor') return Math.max(value, f.cap);
        if (f.capMode === 'ceiling') return Math.min(value, f.cap);
        return value;
    }

    /** Bortz domain contribution (sum of (x-mean)*coeff for indices in that domain). Lower = better. */
    function getBortzDomainContribution(values, featureIndices) {
        if (!window.BortzAge || !window.BortzAge.features || !values || values.length !== window.BortzAge.features.length)
            return NaN;
        var sum = 0;
        for (var i = 0; i < featureIndices.length; i++) {
            var idx = featureIndices[i];
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

    function getPhenoRadarData(athleteData, athleteResults) {
        var labels = [];
        var values = [];
        var athletesWithPheno = (athleteResults || []).filter(function (a) { return a.bestBiomarkerValues && a.bestBiomarkerValues.length; });
        if (athletesWithPheno.length === 0 || !athleteData || !athleteData.bestBiomarkerValues) return null;

        var mv = athleteData.bestBiomarkerValues;
        for (var d = 0; d < PHENO_DOMAINS.length; d++) {
            var dom = PHENO_DOMAINS[d];
            labels.push(dom.label);
            var contributorFn = window.PhenoAge && window.PhenoAge[dom.contributor];
            if (typeof contributorFn !== 'function') {
                values.push(50);
                continue;
            }
            var myScore = contributorFn(mv);
            var allScores = athletesWithPheno.map(function (a) { return contributorFn(a.bestBiomarkerValues); });
            var pct = scoreToPercentile(myScore, allScores);
            values.push(pct !== null ? pct : 50);
        }
        return { labels: labels, values: values };
    }

    function getBortzRadarData(athleteData, athleteResults) {
        var labels = BORTZ_DOMAIN_LABELS.slice();
        var values = [];
        var athletesWithBortz = (athleteResults || []).filter(function (a) { return a.bestBortzValues && a.bestBortzValues.length; });
        if (athletesWithBortz.length === 0 || !athleteData || !athleteData.bestBortzValues) return null;

        var bv = athleteData.bestBortzValues;
        for (var i = 0; i < BORTZ_DOMAIN_LABELS.length; i++) {
            var name = BORTZ_DOMAIN_LABELS[i];
            var indices = BORTZ_DOMAIN_INDICES[name];
            var myScore = getBortzDomainContribution(bv, indices);
            var allScores = athletesWithBortz.map(function (a) { return getBortzDomainContribution(a.bestBortzValues, indices); });
            var pct = scoreToPercentile(myScore, allScores);
            values.push(pct !== null ? pct : 50);
        }
        return { labels: labels, values: values };
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

        var phenoData = hasPheno ? getPhenoRadarData(athleteData, athleteResults) : null;
        var bortzData = hasBortz ? getBortzRadarData(athleteData, athleteResults) : null;
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

        if (!data || !window.Chart || !canvas) {
            wrapper.style.display = 'none';
            fallback.style.display = 'flex';
            fallback.style.flexDirection = 'column';
            fallback.style.alignItems = 'center';
            fallback.style.justifyContent = 'center';
            return;
        }

        wrapper.style.display = 'block';
        fallback.style.display = 'none';

        destroyRadarChart();
        var ctx = canvas.getContext('2d');
        var fontFamily = window.getComputedStyle(document.body).fontFamily || 'system-ui, sans-serif';
        var primaryRgb = '0, 188, 212';
        var secondaryRgb = '255, 64, 129';

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
                layout: { padding: 14 },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        backgroundColor: 'rgba(0,0,0,0.78)',
                        padding: 10,
                        cornerRadius: 8,
                        titleFont: { size: 12, weight: '600' },
                        bodyFont: { size: 12 },
                        callbacks: {
                            label: function (ctx) {
                                return ctx.label + ': ' + ctx.raw + 'th percentile';
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
                            padding: 6,
                            z: 1
                        },
                        grid: { color: 'rgba(0,0,0,0.06)', lineWidth: 1 },
                        angleLines: { color: 'rgba(0,0,0,0.06)', lineWidth: 1 }
                    }
                },
                animation: { duration: 500 },
                hover: { mode: 'nearest' }
            }
        });
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

    window.generateAgeVisualization = generateAgeVisualization;
    window.generateAgeVisualizationInternal = generateAgeVisualizationInternal;
    window.destroyAgeRadarChart = destroyRadarChart;
})();
