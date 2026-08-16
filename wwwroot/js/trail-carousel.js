(function () {
    const carousel = document.getElementById('trailCarousel');
    if (!carousel) return;

    const cards = Array.from(carousel.querySelectorAll('.trail-card'));
    if (!cards.length) return;

    const INTERVAL = 6000;
    let index = cards.findIndex(c => c.classList.contains('is-active'));
    if (index < 0) index = 0;

    let timer = null;
    let paused = false;

    function activate(next) {
        index = (next + cards.length) % cards.length;
        cards.forEach((c, i) => c.classList.toggle('is-active', i === index));
        carousel.style.setProperty('--tone', cards[index].dataset.tone.replace(/,/g, ' '));
    }

    function start() {
        stop();
        if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
        if (window.innerWidth < 768) return;
        timer = setInterval(() => {
            if (!paused) activate(index + 1);
        }, INTERVAL);
    }

    function stop() {
        if (timer) clearInterval(timer);
        timer = null;
    }

    cards.forEach((card, i) => {
        card.addEventListener('click', (e) => {
            if (e.target.closest('.card-link')) return;
            activate(i);
            start();
        });
    });

    carousel.addEventListener('mouseenter', () => { paused = true; });
    carousel.addEventListener('mouseleave', () => { paused = false; });
    carousel.addEventListener('focusin', () => { paused = true; });
    carousel.addEventListener('focusout', () => { paused = false; });

    carousel.addEventListener('keydown', (e) => {
        if (e.key === 'ArrowRight') {
            e.preventDefault();
            activate(index + 1);
            start();
        } else if (e.key === 'ArrowLeft') {
            e.preventDefault();
            activate(index - 1);
            start();
        }
    });

    activate(index);
    start();
    window.addEventListener('resize', start);
})();