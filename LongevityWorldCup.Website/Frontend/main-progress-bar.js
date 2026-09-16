var MAIN_PROGRESS_STAGE_LABELS = ['Test', 'Calculate', 'Submit'];

function setMainProgressAccessibility(container, hidden) {
    if (!container) return;

    container.setAttribute('aria-hidden', hidden ? 'true' : 'false');
    container.toggleAttribute('inert', hidden);
}

(function () {
    var isStickyProgressInitialized = false;

    function initializeStickyProgress() {
        if (isStickyProgressInitialized) return;

        var stickyProgressContentGap = 12;

        setTimeout(function () {
            if (document.body && document.body.classList.contains('play-flow-route')) return;

            // Scroll to the content under the progress bar so it sits just below the sticky header + progress bar (no overscroll)
            var stickyHeader = document.getElementById('site-sticky-header');
            var stickyProgress = document.getElementById('site-sticky-progress');
            var mainProgressBar = document.getElementById('mainProgressBar');
            var mainProgressStyle = mainProgressBar && window.getComputedStyle(mainProgressBar);
            var isMainProgressVisible = mainProgressBar
                && mainProgressStyle.display !== 'none'
                && mainProgressStyle.visibility !== 'hidden'
                && mainProgressBar.offsetHeight > 0;
            if (!isMainProgressVisible) return;

            var contentStart = stickyProgress && stickyProgress.nextElementSibling;
            if (contentStart && contentStart.tagName === 'SCRIPT') contentStart = contentStart.nextElementSibling;
            if (contentStart) {
                var headerHeight = (stickyHeader && stickyHeader.offsetHeight) || 52;
                var progressHeight = 44; /* #site-sticky-progress when visible */
                var topOffset = headerHeight + progressHeight + stickyProgressContentGap;
                var contentTop = contentStart.getBoundingClientRect().top + (window.scrollY || window.pageYOffset);
                var targetScroll = contentTop - topOffset;
                window.scrollTo({ top: Math.max(0, targetScroll), behavior: 'smooth' });
            }
        }, 500);

        // Sticky progress: show only when it would completely cover the in-page progress bar (not the main site header)
        if (!document.getElementById('site-sticky-progress')) return;
        isStickyProgressInitialized = true;

        var progressBarStickyHeight = 44; /* #site-sticky-progress when visible */
        var stickyVisibilityFrame = 0;

        function updateStickyVisibility() {
            var mainProgressBar = document.getElementById('mainProgressBar');
            var stickyHeader = document.getElementById('site-sticky-header');
            var stickyProgress = document.getElementById('site-sticky-progress');
            if (!stickyProgress) return;

            if (!mainProgressBar || mainProgressBar.offsetHeight === 0) {
                stickyProgress.classList.remove('visible');
                stickyProgress.setAttribute('aria-hidden', 'true');
                stickyProgress.style.top = '';
                setMainProgressAccessibility(mainProgressBar, true);
                document.documentElement.classList.remove('sticky-progress-visible');
                document.documentElement.style.scrollPaddingTop = '';
                return;
            }

            var scrollY = window.scrollY || window.pageYOffset;
            var lwcHeight = (stickyHeader && stickyHeader.offsetHeight) || 52;
            var combinedStickyBottom = lwcHeight + progressBarStickyHeight;
            var progressBarBottomDoc = mainProgressBar && mainProgressBar.offsetHeight
                ? mainProgressBar.getBoundingClientRect().bottom + scrollY
                : 1e9;
            var threshold = mainProgressBar && mainProgressBar.offsetHeight
                ? Math.max(0, progressBarBottomDoc - combinedStickyBottom)
                : 1e9;
            if (scrollY >= threshold) {
                stickyProgress.classList.add('visible');
                stickyProgress.setAttribute('aria-hidden', 'false');
                setMainProgressAccessibility(mainProgressBar, true);
                document.documentElement.classList.add('sticky-progress-visible');
                // Flush under LWC bar: no gap
                if (stickyHeader) {
                    stickyProgress.style.top = stickyHeader.offsetHeight + 'px';
                    document.documentElement.style.scrollPaddingTop = (stickyHeader.offsetHeight + stickyProgress.offsetHeight + stickyProgressContentGap) + 'px';
                }
            } else {
                stickyProgress.classList.remove('visible');
                stickyProgress.setAttribute('aria-hidden', 'true');
                setMainProgressAccessibility(mainProgressBar, false);
                document.documentElement.classList.remove('sticky-progress-visible');
                document.documentElement.style.scrollPaddingTop = '';
                stickyProgress.style.top = '';
            }
        }

        function scheduleStickyVisibilityUpdate() {
            if (stickyVisibilityFrame) return;

            stickyVisibilityFrame = window.requestAnimationFrame(function () {
                stickyVisibilityFrame = 0;
                updateStickyVisibility();
            });
        }

        window.LwcStickyProgress = {
            refresh: updateStickyVisibility
        };

        window.addEventListener('scroll', scheduleStickyVisibilityUpdate, { passive: true });
        window.addEventListener('resize', scheduleStickyVisibilityUpdate);
        updateStickyVisibility();
    }

    initializeStickyProgress();
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initializeStickyProgress, { once: true });
    }
})();

// Hides the entire progress‐bar container
function hideMainProgress() {
    var container = document.getElementById('mainProgressBar');
    var stickyProgress = document.getElementById('site-sticky-progress');
    if (container) {
        container.style.display = 'none';
        setMainProgressAccessibility(container, true);
    }
    if (stickyProgress) {
        stickyProgress.classList.remove('visible');
        stickyProgress.setAttribute('aria-hidden', 'true');
        stickyProgress.style.top = '';
    }
    document.documentElement.classList.remove('sticky-progress-visible');
    document.documentElement.style.scrollPaddingTop = '';
}

// Function to update main progress bar and sticky row based on the current stage
function updateMainProgress(currentStage) {
    var progressFill = document.getElementById('progressFill');
    var stages = document.querySelectorAll('.progress-container .stage');

    if (stages.length === 0) return;

    // Update in-page stages
    stages.forEach(function (s, index) {
        if (index < currentStage - 1) {
            s.className = 'stage completed';
        } else if (index === currentStage - 1) {
            s.className = 'stage active';
        } else {
            s.className = 'stage';
        }
    });

    // Progress fill (kept for any code that may rely on it; bar is hidden)
    var mainProgressPercentage = ((currentStage - 1) / (stages.length - 1)) * 100;
    if (progressFill) progressFill.style.width = mainProgressPercentage + '%';

    // Update sticky progress row: label + dot states
    var stickyLabel = document.getElementById('stickyStageLabel');
    var stickyDots = document.querySelectorAll('#site-sticky-progress .sticky-dot');
    var stickyRow = document.getElementById('site-sticky-progress');

    if (stickyLabel && stickyRow && currentStage >= 1 && currentStage <= 3) {
        var label = MAIN_PROGRESS_STAGE_LABELS[currentStage - 1];
        stickyLabel.textContent = label;
        stickyRow.setAttribute('aria-label', 'Current step: ' + label);
    }

    if (stickyDots && stickyDots.length === 3) {
        stickyDots.forEach(function (dot, index) {
            var i = index + 1;
            dot.classList.remove('completed', 'active');
            if (i < currentStage) dot.classList.add('completed');
            else if (i === currentStage) dot.classList.add('active');
        });
    }
}
