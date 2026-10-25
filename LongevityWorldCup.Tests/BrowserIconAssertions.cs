namespace LongevityWorldCup.Tests;

internal static class BrowserIconAssertions
{
    // Measure painted artwork rather than the font host or an SVG's empty viewBox.
    internal const string Ink = """
        host => {
            if (!host) return null;
            const visible = element => {
                const style = getComputedStyle(element), box = element.getBoundingClientRect();
                return style.display !== 'none' && style.visibility === 'visible' && Number(style.opacity) > 0
                    && box.width > 0 && box.height > 0;
            };
            if (!visible(host)) return null;
            const svg = host.matches('svg') ? host : host.querySelector('svg');
            if (!svg) {
                const style = getComputedStyle(host, '::before');
                if (['', 'none', 'normal', '""'].includes(style.content)
                    || !document.fonts.check(`${style.fontWeight} ${style.fontSize} ${style.fontFamily}`)) return null;
                return host.getBoundingClientRect().toJSON();
            }
            if (!visible(svg)) return null;
            const boxes = [...svg.querySelectorAll('path,circle,ellipse,rect,line,polyline,polygon,use')].flatMap(shape => {
                const style = getComputedStyle(shape), bounds = shape.getBBox();
                const paint = (color, opacity) => Number(opacity) > 0 && !['none','transparent','rgba(0, 0, 0, 0)'].includes(color);
                if (style.visibility !== 'visible' || Number(style.opacity) <= 0 || !(bounds.width || bounds.height)
                    || !(paint(style.fill, style.fillOpacity) || paint(style.stroke, style.strokeOpacity))) return [];
                const rect = shape.getBoundingClientRect(), matrix = shape.getScreenCTM();
                const strokeWidth = shape.computedStyleMap().get('stroke-width').to('px').value;
                const pad = paint(style.stroke, style.strokeOpacity) ? strokeWidth / 2
                    * Math.max(Math.hypot(matrix.a, matrix.b), Math.hypot(matrix.c, matrix.d)) : 0;
                return [{left:rect.left-pad,right:rect.right+pad,top:rect.top-pad,bottom:rect.bottom+pad}];
            });
            if (!boxes.length) return null;
            const left = Math.min(...boxes.map(b => b.left)), right = Math.max(...boxes.map(b => b.right));
            const top = Math.min(...boxes.map(b => b.top)), bottom = Math.max(...boxes.map(b => b.bottom));
            return {left,right,top,bottom,width:right-left,height:bottom-top};
        }
        """;
}
