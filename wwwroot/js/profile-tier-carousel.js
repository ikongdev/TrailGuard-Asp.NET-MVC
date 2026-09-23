(function () {
    var carousel = document.querySelector('[data-tier-carousel]');
    if (!carousel) return;




    if (carousel.dataset.tierCarouselInitialized === 'true') return;
    carousel.dataset.tierCarouselInitialized = 'true';

    var slides = Array.from(carousel.querySelectorAll('[data-tier-slide]'));
    var prevButton = carousel.querySelector('[data-tier-prev]');
    var nextButton = carousel.querySelector('[data-tier-next]');
    if (!slides.length || !prevButton || !nextButton) return;

    var count = slides.length;





    var parsedInitialIndex = parseInt(carousel.dataset.initialIndex, 10);
    var index = (!isNaN(parsedInitialIndex) && parsedInitialIndex >= 0 && parsedInitialIndex < count)
        ? parsedInitialIndex
        : 0;

    var prefersReducedMotion = !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);






    var isAnimating = false;
    var TRANSITION_MS = 100;




    var CLASS_HIDDEN = 'hidden';
    var CLASS_OPACITY_VISIBLE = 'opacity-100';
    var CLASS_OPACITY_HIDDEN = 'opacity-0';
    var CLASS_X_REST = 'translate-x-0';
    var CLASS_X_RIGHT = 'translate-x-3';
    var CLASS_X_LEFT = '-translate-x-3';

    function settle(slide) {
        slide.classList.remove(CLASS_OPACITY_HIDDEN, CLASS_X_RIGHT, CLASS_X_LEFT);
        slide.classList.add(CLASS_OPACITY_VISIBLE, CLASS_X_REST);
    }

    function updateButtons() {
        prevButton.disabled = index <= 0;
        nextButton.disabled = index >= count - 1;
    }




    function showImmediately(newIndex) {
        for (var i = 0; i < count; i++) {
            var isActive = i === newIndex;
            slides[i].classList.toggle(CLASS_HIDDEN, !isActive);
            if (isActive) settle(slides[i]);
        }
        index = newIndex;
        updateButtons();
    }








    function animateTo(newIndex) {
        var forward = newIndex > index;
        var oldSlide = slides[index];
        var newSlide = slides[newIndex];
        isAnimating = true;



        oldSlide.classList.remove(CLASS_OPACITY_VISIBLE, CLASS_X_REST);
        oldSlide.classList.add(CLASS_OPACITY_HIDDEN, forward ? CLASS_X_LEFT : CLASS_X_RIGHT);

        setTimeout(function () {



            oldSlide.classList.add(CLASS_HIDDEN);
            settle(oldSlide);

            newSlide.classList.remove(CLASS_HIDDEN);
            newSlide.classList.remove(CLASS_OPACITY_VISIBLE, CLASS_X_REST);
            newSlide.classList.add(CLASS_OPACITY_HIDDEN, forward ? CLASS_X_RIGHT : CLASS_X_LEFT);

            index = newIndex;





            requestAnimationFrame(function () {
                requestAnimationFrame(function () {
                    settle(newSlide);

                    setTimeout(function () {
                        isAnimating = false;
                        updateButtons();
                    }, TRANSITION_MS);
                });
            });
        }, TRANSITION_MS);
    }

    function go(nextIndex) {
        if (isAnimating) return;
        if (nextIndex < 0 || nextIndex >= count || nextIndex === index) return;

        if (prefersReducedMotion) {
            showImmediately(nextIndex);
            return;
        }

        animateTo(nextIndex);
    }

    prevButton.addEventListener('click', function () { go(index - 1); });
    nextButton.addEventListener('click', function () { go(index + 1); });




    carousel.addEventListener('keydown', function (e) {
        if (e.key === 'ArrowLeft') {
            e.preventDefault();
            go(index - 1);
        } else if (e.key === 'ArrowRight') {
            e.preventDefault();
            go(index + 1);
        }
    });




    updateButtons();
})();
