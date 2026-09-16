if (!window.__eventsEmbedListener) {
    window.__eventsEmbedListener = 1;
    window.addEventListener('message', function (e) {
        var d = e.data;
        if (d && d.type === 'events-embed-height') {
            var f = document.getElementById('events-frame');
            if (f && e.source === f.contentWindow) f.style.height = d.h + 'px';
        }
    })
}

(function () {
    function sendThemeToFrame(f) {
        if (!f || !f.contentWindow) return;
        var cs = getComputedStyle(document.documentElement);
        var bs = getComputedStyle(document.body);
        var vars = ['--primary-color', '--secondary-color', '--card-bg', '--dark-text-color', '--light-text-color', '--body-bg'];
        var payload = { type: 'events-theme-vars', 'font-family': bs.getPropertyValue('font-family'), 'background-color': bs.getPropertyValue('background-color') };
        vars.forEach(function (v) { payload[v] = cs.getPropertyValue(v) });
        if (f.closest && f.closest('#detailsModal')) {
            var modal = document.getElementById('detailsModal');
            var ms = modal ? getComputedStyle(modal) : null;
            payload['--card-bg'] = 'rgba(255,255,255,.045)';
            payload['--dark-text-color'] = ms ? ms.getPropertyValue('--athlete-modal-text') : 'rgba(255,255,255,.94)';
            payload['--body-bg'] = 'transparent';
            payload['--zebra-even'] = 'transparent';
            payload['--zebra-odd'] = 'transparent';
            payload['--details-bg'] = 'rgba(246,244,237,.06)';
            payload['--events-text'] = ms ? ms.getPropertyValue('--athlete-modal-text') : 'rgba(255,255,255,.94)';
            payload['--events-muted'] = ms ? ms.getPropertyValue('--athlete-modal-muted') : 'rgba(255,255,255,.68)';
            payload['--events-border'] = ms ? ms.getPropertyValue('--athlete-modal-border') : 'rgba(255,255,255,.16)';
            payload['--events-hover-bg'] = 'rgba(246,244,237,.07)';
            payload['--events-header-bg'] = 'rgba(246,244,237,.06)';
            payload['--event-link'] = 'rgba(213,224,221,.92)';
        }
        f.contentWindow.postMessage(payload, '*');
    }
    window.__sendThemeToEventsFrame = sendThemeToFrame;
    var frame = document.getElementById('events-frame');
    if (frame) {
        frame.addEventListener('load', function () { sendThemeToFrame(frame) });
    }
})();
