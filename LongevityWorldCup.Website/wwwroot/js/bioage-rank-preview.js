(function () {
    'use strict';

    var sharedAthletesPromise = null;

    function getSharedAthletes() {
        if (window.getSharedAthletes) return window.getSharedAthletes();
        if (!sharedAthletesPromise) {
            sharedAthletesPromise = fetch('/api/data/athletes', { headers: { accept: 'application/json' } })
                .then(function (response) {
                    return response.ok ? response.json() : Promise.reject(response.status);
                })
                .catch(function (error) {
                    sharedAthletesPromise = null;
                    throw error;
                });
        }
        return sharedAthletesPromise;
    }

    function isCompletePhenoBiomarkerSet(set) {
        if (!set || !set.Date) return false;
        var values = [
            set.Wbc1000cellsuL,
            set.LymPc,
            set.McvFL,
            set.RdwPc,
            set.AlbGL,
            set.AlpUL,
            set.CreatUmolL,
            set.GluMmolL,
            set.CrpMgL
        ];
        return values.every(Number.isFinite) && set.CrpMgL > 0;
    }

    function isCompleteBortzBiomarkerSet(set) {
        if (!set || !set.Date) return false;
        var values = [
            set.AlbGL,
            set.AlpUL,
            set.UreaMmolL,
            set.CholesterolMmolL,
            set.CreatUmolL,
            set.CystatinCMgL,
            set.Hba1cMmolMol,
            set.CrpMgL,
            set.GgtUL,
            set.Rbc10e12L,
            set.McvFL,
            set.RdwPc,
            set.Wbc1000cellsuL,
            set.MonocytePc,
            set.NeutrophilPc,
            set.LymPc,
            set.AltUL,
            set.ShbgNmolL,
            set.VitaminDNmolL,
            set.GluMmolL,
            set.MchPg,
            set.ApoA1GL
        ];
        return values.every(Number.isFinite) && set.CrpMgL > 0;
    }

    function calculateAgeAtDate(dob, date) {
        if (typeof window.calculateAgeAtDate === 'function') {
            return window.calculateAgeAtDate(dob, date);
        }

        var age = date.getFullYear() - dob.getFullYear();
        var monthDiff = date.getMonth() - dob.getMonth();
        if (monthDiff < 0 || (monthDiff === 0 && date.getDate() < dob.getDate())) {
            age--;
        }
        return age;
    }

    function getDisplayName(athlete) {
        return athlete && athlete.DisplayName && athlete.DisplayName.trim()
            ? athlete.DisplayName.trim()
            : athlete.Name;
    }

    function mapAthlete(athlete) {
        if (!athlete || !athlete.DateOfBirth || !Array.isArray(athlete.Biomarkers)) return null;
        var dob = new Date(athlete.DateOfBirth.Year, athlete.DateOfBirth.Month - 1, athlete.DateOfBirth.Day);
        var displayName = getDisplayName(athlete);
        var phenoSummary = getPhenoSummary(athlete, dob);
        var bortzSummary = getBortzSummary(athlete, dob);

        if (!phenoSummary && !bortzSummary) return null;

        return {
            slug: athlete.AthleteSlug || '',
            name: athlete.Name || displayName,
            displayName: displayName,
            dateOfBirth: dob,
            ageReduction: phenoSummary ? phenoSummary.ageReduction : null,
            bortzAgeReduction: bortzSummary ? bortzSummary.ageReduction : null
        };
    }

    function getPhenoSummary(athlete, dob) {
        if (!window.PhenoAge || typeof window.PhenoAge.calculatePhenoAge !== 'function') return null;

        var bestAge = Infinity;
        var bestChrono = null;
        athlete.Biomarkers.forEach(function (entry) {
            if (!isCompletePhenoBiomarkerSet(entry)) return;

            var ageAtEntry = calculateAgeAtDate(dob, new Date(entry.Date));
            var values = [
                ageAtEntry,
                entry.AlbGL,
                entry.CreatUmolL,
                entry.GluMmolL,
                Math.log(entry.CrpMgL / 10),
                entry.Wbc1000cellsuL,
                entry.LymPc,
                entry.McvFL,
                entry.RdwPc,
                entry.AlpUL
            ];
            if (!values.every(Number.isFinite)) return;

            var phenoAge = window.PhenoAge.calculatePhenoAge(values);
            if (Number.isFinite(phenoAge) && phenoAge < bestAge) {
                bestAge = phenoAge;
                bestChrono = ageAtEntry;
            }
        });

        return Number.isFinite(bestAge) && Number.isFinite(bestChrono)
            ? { ageReduction: bestAge - bestChrono }
            : null;
    }

    function getBortzSummary(athlete, dob) {
        if (!window.BortzAge || typeof window.BortzAge.calculateBortzAge !== 'function') return null;

        var bestAge = Infinity;
        var bestChrono = null;
        athlete.Biomarkers.forEach(function (entry) {
            if (!isCompleteBortzBiomarkerSet(entry)) return;

            var ageAtEntry = calculateAgeAtDate(dob, new Date(entry.Date));
            var wbc = entry.Wbc1000cellsuL;
            var values = [
                ageAtEntry,
                entry.AlbGL,
                entry.AlpUL,
                entry.UreaMmolL,
                entry.CholesterolMmolL,
                entry.CreatUmolL,
                entry.CystatinCMgL,
                entry.Hba1cMmolMol,
                entry.CrpMgL,
                entry.GgtUL,
                entry.Rbc10e12L,
                entry.McvFL,
                entry.RdwPc,
                wbc * entry.MonocytePc / 100,
                wbc * entry.NeutrophilPc / 100,
                entry.LymPc,
                entry.AltUL,
                entry.ShbgNmolL,
                entry.VitaminDNmolL,
                entry.GluMmolL,
                entry.MchPg,
                entry.ApoA1GL
            ];
            if (!values.every(Number.isFinite)) return;

            var bortzAge = window.BortzAge.calculateBortzAge(ageAtEntry, values);
            if (Number.isFinite(bortzAge) && bortzAge < bestAge) {
                bestAge = bortzAge;
                bestChrono = ageAtEntry;
            }
        });

        return Number.isFinite(bestAge) && Number.isFinite(bestChrono)
            ? { ageReduction: bestAge - bestChrono }
            : null;
    }

    function compareByClock(clock) {
        return function (a, b) {
            var aReduction = clock === 'bortz' ? a.bortzAgeReduction : a.ageReduction;
            var bReduction = clock === 'bortz' ? b.bortzAgeReduction : b.ageReduction;

            if (aReduction < bReduction) return -1;
            if (aReduction > bReduction) return 1;
            if (a.dateOfBirth < b.dateOfBirth) return -1;
            if (a.dateOfBirth > b.dateOfBirth) return 1;
            if (a.name < b.name) return -1;
            if (a.name > b.name) return 1;
            return 0;
        };
    }

    function formatReduction(value, precision) {
        if (!Number.isFinite(value)) return '';
        return (value > 0 ? '+' : '') + value.toFixed(precision) + 'y';
    }

    function getReduction(row, clock) {
        return clock === 'bortz' ? row.bortzAgeReduction : row.ageReduction;
    }

    function chooseReductionPrecision(rows, clock) {
        var values = rows
            .map(function (row) { return getReduction(row, clock); })
            .filter(Number.isFinite);
        var maxPrecision = 6;

        for (var precision = 1; precision <= maxPrecision; precision++) {
            var buckets = Object.create(null);
            var hasHiddenDifference = values.some(function (value) {
                var key = value.toFixed(precision);
                var bucket = buckets[key] || (buckets[key] = []);
                var differs = bucket.some(function (existing) {
                    return Math.abs(existing - value) > 1e-9;
                });
                bucket.push(value);
                return differs;
            });

            if (!hasHiddenDifference) return precision;
        }

        return maxPrecision;
    }

    function escapeHtml(text) {
        return String(text)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function ordinal(value) {
        var n = Math.abs(Math.round(value));
        var mod100 = n % 100;
        if (mod100 >= 11 && mod100 <= 13) return n + 'th';
        switch (n % 10) {
            case 1: return n + 'st';
            case 2: return n + 'nd';
            case 3: return n + 'rd';
            default: return n + 'th';
        }
    }

    function buildNearbyRows(rows, youIndex, clock) {
        var start = Math.max(0, youIndex - 2);
        var end = Math.min(rows.length, youIndex + 3);
        var visibleRows = rows.slice(start, end);
        var precision = chooseReductionPrecision(visibleRows, clock);
        var html = '';
        for (var i = start; i < end; i++) {
            var row = rows[i];
            var isYou = row.isYou;
            var reduction = getReduction(row, clock);
            var nameHtml = escapeHtml(isYou ? 'You' : row.displayName);
            html += '<div class="bioage-rank-row' + (isYou ? ' current' : '') + '">' +
                '<span class="bioage-rank-row-place">#' + (i + 1) + '</span>' +
                '<span class="bioage-rank-row-name">' + nameHtml + '</span>' +
                '<span class="bioage-rank-row-score">' + escapeHtml(formatReduction(reduction, precision)) + '</span>' +
                '</div>';
        }
        return html;
    }

    function buildHtml(clock, rank, rankedFieldSize, topPercent, rows, youIndex) {
        var title = clock === 'bortz' ? 'Hypothetical Bortz Age rank' : 'Hypothetical Pheno Age rank';
        var leagueName = clock === 'bortz' ? 'Bortz Age' : 'Pheno Age';
        return '<div class="bioage-rank-summary">' +
            '<div class="bioage-rank-number">#' + rank + '</div>' +
            '<div class="bioage-rank-copy">' +
            '<div class="bioage-rank-label">' + title + '</div>' +
            '<div class="bioage-rank-meta">' + leagueName + ' &middot; ' + rank + ' of ' + rankedFieldSize + '</div>' +
            '<div class="bioage-rank-percentile">Top ' + topPercent + '%</div>' +
            '</div>' +
            '</div>' +
            '<div class="bioage-rank-neighbors">' + buildNearbyRows(rows, youIndex, clock) + '</div>';
    }

    function render(targetId, options) {
        var target = document.getElementById(targetId);
        if (!target || !options) return Promise.resolve();

        var clock = options.clock === 'bortz' ? 'bortz' : 'pheno';
        var ageReduction = Number(options.ageReduction);
        if (!Number.isFinite(ageReduction) || !(options.dateOfBirth instanceof Date)) {
            target.hidden = true;
            target.innerHTML = '';
            return Promise.resolve();
        }

        target.hidden = false;
        target.innerHTML = '<div class="bioage-rank-loading">Calculating rank...</div>';

        return getSharedAthletes()
            .then(function (athletes) {
                var summaries = (athletes || []).map(mapAthlete).filter(Boolean);
                var field = summaries.filter(function (athlete) {
                    var value = clock === 'bortz' ? athlete.bortzAgeReduction : athlete.ageReduction;
                    return Number.isFinite(value);
                });

                var you = {
                    name: 'You',
                    displayName: 'You',
                    dateOfBirth: options.dateOfBirth,
                    ageReduction: clock === 'pheno' ? ageReduction : null,
                    bortzAgeReduction: clock === 'bortz' ? ageReduction : null,
                    isYou: true
                };
                var rows = field.concat([you]).sort(compareByClock(clock));
                var youIndex = rows.indexOf(you);
                if (youIndex < 0) throw new Error('Unable to place rank preview row.');

                var rank = youIndex + 1;
                var rankedFieldSize = rows.length;
                var topPercent = Math.max(1, Math.ceil(rank / rankedFieldSize * 100));
                target.innerHTML = buildHtml(clock, rank, rankedFieldSize, topPercent, rows, youIndex);
            })
            .catch(function () {
                target.hidden = true;
                target.innerHTML = '';
            });
    }

    function clear(targetId) {
        var target = document.getElementById(targetId);
        if (!target) return;
        target.hidden = true;
        target.innerHTML = '';
    }

    window.LwcBioAgeRankPreview = {
        render: render,
        clear: clear
    };
})();
