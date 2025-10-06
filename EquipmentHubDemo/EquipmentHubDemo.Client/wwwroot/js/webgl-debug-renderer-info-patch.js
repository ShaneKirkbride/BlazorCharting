(function () {
    "use strict";

    var DEBUG_RENDERER_VENDOR = 0x9245;
    var DEBUG_RENDERER_RENDERER = 0x9246;

    function patchPrototype(proto) {
        if (!proto || typeof proto.getParameter !== "function" || proto.__ehdWebGlPatched) {
            return;
        }

        var original = proto.getParameter;
        proto.getParameter = function (parameter) {
            if (parameter === DEBUG_RENDERER_VENDOR || parameter === DEBUG_RENDERER_RENDERER) {
                var extension = null;
                try {
                    if (typeof this.getExtension === "function") {
                        extension = this.getExtension("WEBGL_debug_renderer_info");
                    }
                } catch (error) {
                    // Ignore errors thrown when probing for the extension.
                }

                if (!extension) {
                    return "Unavailable";
                }

                if (parameter === DEBUG_RENDERER_VENDOR) {
                    return original.call(this, extension.UNMASKED_VENDOR_WEBGL);
                }

                return original.call(this, extension.UNMASKED_RENDERER_WEBGL);
            }

            return original.call(this, parameter);
        };

        Object.defineProperty(proto, "__ehdWebGlPatched", {
            value: true,
            configurable: false,
            enumerable: false,
            writable: false
        });
    }

    patchPrototype(typeof WebGLRenderingContext !== "undefined" ? WebGLRenderingContext.prototype : null);
    patchPrototype(typeof WebGL2RenderingContext !== "undefined" ? WebGL2RenderingContext.prototype : null);
})();
