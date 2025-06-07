window.svgInterop = {
    getSvgPoint: function (svg, clientX, clientY) {
        let point = svg.createSVGPoint();
        point.x = clientX;
        point.y = clientY;
        return point.matrixTransform(svg.getScreenCTM().inverse());
    },
    getMousePosition: function (svg, clientX, clientY) {
        const rect = svg.getBoundingClientRect();
        const scale = 220 / rect.width; // viewBox is 220x220
        return {
            x: (clientX - rect.left) * scale - 110,
            y: (clientY - rect.top) * scale - 110
        };
    }
};

window.getBoundingClientRect = function (element) {
    return element.getBoundingClientRect();
}; 