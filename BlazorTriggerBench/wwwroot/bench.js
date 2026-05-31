// Perf instrumentation for the Blazor statement-row bench (rAF FPS + jump-scroll stress + DOM count).
// Mirrors ReactorPerfLab's 跳变压测: teleport scrollTop across the whole range every frame (the
// aggressive recycling case) and sample frame times. Blazor FPS standard differs from WinUI's
// CompositionTarget.Rendering, but rAF here is the closest comparable client-side frame metric.
(function () {
    // Live FPS overlay (rAF).
    let last = performance.now(), frames = 0;
    function loop(t) {
        frames++;
        if (t - last >= 500) {
            const fps = frames * 1000 / (t - last);
            frames = 0; last = t;
            const el = document.getElementById('liveFps');
            if (el) el.textContent = 'live FPS: ' + fps.toFixed(0);
        }
        requestAnimationFrame(loop);
    }
    requestAnimationFrame(loop);

    function vdc(n) { let q = 0, b = 0.5; while (n > 0) { q += (n & 1) * b; n >>= 1; b *= 0.5; } return q; }

    window.bench = {
        domCount: function () {
            const list = document.getElementById('teList');
            return list ? list.querySelectorAll('*').length : 0;
        },
        // Virtualize populates after the container is sized + a couple frames; wait so the count is real.
        domCountSettled: function () {
            return new Promise(function (res) {
                requestAnimationFrame(function () {
                    requestAnimationFrame(function () {
                        const list = document.getElementById('teList');
                        res(list ? list.querySelectorAll('*').length : 0);
                    });
                });
            });
        },
        // Continuous fast jump-scroll for durationMs; samples per-frame fps; returns aggregates.
        autoScroll: function (durationMs) {
            const list = document.getElementById('teList');
            if (!list) return Promise.resolve(null);
            return new Promise(function (resolve) {
                let i = 0, samples = 0, sumFps = 0, minFps = 1e9, worst = 0;
                const start = performance.now();
                let lastFrame = start;
                function step(now) {
                    const dt = now - lastFrame; lastFrame = now;
                    if (dt > 0) {
                        const f = 1000 / dt;
                        samples++; sumFps += f;
                        if (f < minFps) minFps = f;
                        if (dt > worst) worst = dt;
                    }
                    const max = list.scrollHeight - list.clientHeight;
                    list.scrollTop = vdc(++i) * Math.max(0, max);
                    if (now - start >= durationMs) {
                        resolve({
                            avg: samples ? sumFps / samples : 0,
                            min: minFps === 1e9 ? 0 : minFps,
                            worstMs: worst,
                            dom: list.querySelectorAll('*').length,
                            samples: samples
                        });
                    } else {
                        requestAnimationFrame(step);
                    }
                }
                requestAnimationFrame(step);
            });
        }
    };
})();
