(function () {
    const canvas = document.getElementById('shooting-stars');
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (reduceMotion) return;

    let width, height, vanishX, vanishY;

    function resize() {
        width = canvas.width = canvas.offsetWidth;
        height = canvas.height = canvas.offsetHeight;
        vanishX = width * 0.5;
        vanishY = height * 0.95;
    }

    resize();
    window.addEventListener('resize', resize);

    const stars = [];
    const MAX_STARS = 50;

    function spawn() {
        const startX = Math.random() * width * 1.4 - width * 0.2;
        const startY = -Math.random() * height * 0.3;

        const dx = vanishX - startX;
        const dy = vanishY - startY;
        const dist = Math.hypot(dx, dy);

        stars.push({
            x: startX,
            y: startY,
            vx: (dx / dist),
            vy: (dy / dist),
            speed: 2.6 + Math.random() * 2.2,
            life: 0,
            maxLife: 70 + Math.random() * 50,
            length: 60 + Math.random() * 70,
            alpha: 0.35 + Math.random() * 0.3
        });
    }

    function draw() {
        ctx.clearRect(0, 0, width, height);

        for (let i = stars.length - 1; i >= 0; i--) {
            const s = stars[i];
            s.life++;

            const progress = s.life / s.maxLife;
            if (progress >= 1) {
                stars.splice(i, 1);
                continue;
            }

            const shrink = 1 - progress * 0.75;
            const step = s.speed * shrink;

            s.x += s.vx * step;
            s.y += s.vy * step;

            let fade;
            if (progress < 0.15) {
                fade = progress / 0.15;
            } else {
                fade = 1 - (progress - 0.15) / 0.85;
            }

            const tailLength = s.length * shrink;
            const tailX = s.x - s.vx * tailLength;
            const tailY = s.y - s.vy * tailLength;

            const gradient = ctx.createLinearGradient(s.x, s.y, tailX, tailY);
            gradient.addColorStop(0, `rgba(255, 255, 255, ${s.alpha * fade})`);
            gradient.addColorStop(1, 'rgba(255, 255, 255, 0)');

            ctx.strokeStyle = gradient;
            ctx.lineWidth = Math.max(0.4, 1.1 * shrink);
            ctx.lineCap = 'round';
            ctx.beginPath();
            ctx.moveTo(s.x, s.y);
            ctx.lineTo(tailX, tailY);
            ctx.stroke();
        }

        if (stars.length < MAX_STARS && Math.random() < 0.7) {
            spawn();
        }

        requestAnimationFrame(draw);
    }

    for (let i = 0; i < 5; i++) spawn();
    draw();
})();