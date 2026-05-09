(function () {
    'use strict';

    // Disables submit button + sets aria-busy while a form is submitting.
    // Markup contract: <form data-busy>...</form>
    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('form[data-busy]').forEach(function (form) {
            form.addEventListener('submit', function () {
                var btns = form.querySelectorAll('button[type="submit"]');
                btns.forEach(function (b) {
                    b.setAttribute('aria-busy', 'true');
                    b.dataset.originalText = b.dataset.originalText || b.textContent;
                    b.textContent = 'Сохраняем…';
                    setTimeout(function () { b.disabled = true; }, 0);
                });
            });
        });
    });
})();
