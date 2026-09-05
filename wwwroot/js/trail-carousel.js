(function () {
    'use strict';

    const carousel = document.getElementById('trailCarousel');
    if (!carousel) return;

    const track = carousel.querySelector('.carousel-track');
    const cards = Array.from(carousel.querySelectorAll('.trail-card'));
    if (!track || !cards.length) return;

    const indicatorCurrent = document.getElementById('trailCarouselIndicatorCurrent');
    const indicatorTotal = document.getElementById('trailCarouselIndicatorTotal');
    const indicatorAnnouncement = document.getElementById('trailCarouselIndicatorAnnouncement');

    const AUTOPLAY_INTERVAL = 6000;
    const ANNOUNCE_DELAY = 250;
    const RESIZE_DEBOUNCE = 150;
    const MOBILE_BREAKPOINT = 768;

    let index = cards.findIndex((c) => c.classList.contains('is-active'));
    if (index < 0) index = 0;

    let autoplayTimer = null;
    let paused = false;
    let scrollFrame = null;
    let announceTimer = null;
    let resizeTimer = null;

    function isMobile() {
        return window.innerWidth < MOBILE_BREAKPOINT;
    }

    function prefersReducedMotion() {
        return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    }

    function normalize(i) {
        return (i + cards.length) % cards.length;
    }

    function scheduleAnnouncement(i) {
        if (!indicatorAnnouncement) return;
        if (announceTimer) clearTimeout(announceTimer);
        announceTimer = setTimeout(() => {
            announceTimer = null;
            indicatorAnnouncement.textContent = 'Popular trail ' + (i + 1) + ' of ' + cards.length;
        }, ANNOUNCE_DELAY);
    }

    // silent=true skips the live-region announcement - used only for the initial
    // render so the page doesn't narrate "trail 1 of 6" the instant it loads.
    function applyActiveState(i, silent) {
        cards.forEach((card, cardIndex) => {
            const active = cardIndex === i;
            card.classList.toggle('is-active', active);
            if (active) {
                card.setAttribute('aria-current', 'true');
            } else {
                card.removeAttribute('aria-current');
            }
        });
        carousel.style.setProperty('--tone', cards[i].dataset.tone.replace(/,/g, ' '));
        if (indicatorCurrent) indicatorCurrent.textContent = String(i + 1);
        if (!silent) scheduleAnnouncement(i);
    }

    function cardCenterX(card) {
        const rect = card.getBoundingClientRect();
        return rect.left + rect.width / 2;
    }

    // Centers a card by moving the track's own scrollLeft (never scrollIntoView,
    // which could drag the whole document vertically).
    function centerCard(i, instant) {
        const card = cards[i];
        const trackRect = track.getBoundingClientRect();
        const cardRect = card.getBoundingClientRect();
        const cardLeftInTrack = cardRect.left - trackRect.left + track.scrollLeft;
        const target = cardLeftInTrack + cardRect.width / 2 - trackRect.width / 2;
        const max = track.scrollWidth - track.clientWidth;
        const left = Math.max(0, Math.min(target, max));
        track.scrollTo({
            left,
            behavior: instant || prefersReducedMotion() ? 'auto' : 'smooth',
        });
    }

    // Single entry point for every state change: tap, keyboard, autoplay, and
    // native-scroll sync all funnel through here so is-active/--tone/the
    // indicator can never drift out of sync with each other.
    function select(next, options) {
        const opts = options || {};
        index = normalize(next);
        applyActiveState(index);
        if (opts.center && isMobile()) {
            centerCard(index, !!opts.instant);
        }
    }

    function startAutoplay() {
        stopAutoplay();
        if (prefersReducedMotion() || isMobile()) return;
        autoplayTimer = setInterval(() => {
            if (!paused) select(index + 1, { center: false });
        }, AUTOPLAY_INTERVAL);
    }

    function stopAutoplay() {
        if (autoplayTimer) clearInterval(autoplayTimer);
        autoplayTimer = null;
    }

    function nearestCardToCenter() {
        const trackRect = track.getBoundingClientRect();
        const centerX = trackRect.left + trackRect.width / 2;
        let nearest = index;
        let minDistance = Infinity;
        cards.forEach((card, i) => {
            const distance = Math.abs(cardCenterX(card) - centerX);
            if (distance < minDistance) {
                minDistance = distance;
                nearest = i;
            }
        });
        return nearest;
    }

    // Native swipe changes scrollLeft on its own; this keeps is-active/--tone/the
    // indicator following whichever card is actually centered, without ever
    // issuing a programmatic scroll itself (that would fight the user's swipe).
    function onTrackScroll() {
        if (!isMobile()) return;
        if (scrollFrame) return;
        scrollFrame = requestAnimationFrame(() => {
            scrollFrame = null;
            const nearest = nearestCardToCenter();
            if (nearest !== index) {
                select(nearest, { center: false });
            }
        });
    }

    cards.forEach((card, i) => {
        card.addEventListener('click', (e) => {
            if (e.target.closest('.card-link')) return;
            if (isMobile()) {
                if (i === index) return;
                select(i, { center: true });
                return;
            }
            select(i, { center: false });
            startAutoplay();
        });
    });

    carousel.addEventListener('mouseenter', () => { paused = true; });
    carousel.addEventListener('mouseleave', () => { paused = false; });
    carousel.addEventListener('focusin', () => { paused = true; });
    carousel.addEventListener('focusout', () => { paused = false; });

    carousel.addEventListener('keydown', (e) => {
        if (e.key !== 'ArrowRight' && e.key !== 'ArrowLeft') return;
        e.preventDefault();
        const dir = e.key === 'ArrowRight' ? 1 : -1;
        select(index + dir, { center: true });
        if (!isMobile()) startAutoplay();
    });

    track.addEventListener('scroll', onTrackScroll, { passive: true });

    let wasMobile = isMobile();

    function handleResize() {
        const mobileNow = isMobile();
        if (mobileNow) {
            stopAutoplay();
            if (!wasMobile) {
                // Just crossed into mobile - snap the current card to center
                // instantly. Re-centering on every subsequent mobile resize
                // (e.g. a keyboard opening) would fight the user's own swipe.
                requestAnimationFrame(() => centerCard(index, true));
            }
        } else {
            startAutoplay();
        }
        wasMobile = mobileNow;
    }

    window.addEventListener('resize', () => {
        if (resizeTimer) clearTimeout(resizeTimer);
        resizeTimer = setTimeout(handleResize, RESIZE_DEBOUNCE);
    });

    if (indicatorTotal) indicatorTotal.textContent = String(cards.length);
    applyActiveState(index, true);
    if (wasMobile) {
        requestAnimationFrame(() => centerCard(index, true));
    } else {
        startAutoplay();
    }
})();
