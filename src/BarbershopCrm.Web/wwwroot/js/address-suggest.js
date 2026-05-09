(function () {
    'use strict';

    // Wires an <input data-address-suggest> to the /api/address/suggest endpoint
    // and writes the chosen formatted address + lat/lng into hidden fields.
    // Markup contract:
    //   <input data-address-suggest data-lat-target="#latId" data-lon-target="#lonId" />
    //   <div class="address-suggest__list" data-suggest-list></div>  (sibling, optional - auto-created)

    function initInput(input) {
        if (input.dataset.suggestInited === '1') return;
        input.dataset.suggestInited = '1';
        input.setAttribute('autocomplete', 'off');

        var latSel = input.dataset.latTarget;
        var lonSel = input.dataset.lonTarget;
        var latEl = latSel ? document.querySelector(latSel) : null;
        var lonEl = lonSel ? document.querySelector(lonSel) : null;

        var wrap = document.createElement('div');
        wrap.className = 'address-suggest';
        input.parentNode.insertBefore(wrap, input);
        wrap.appendChild(input);

        var list = document.createElement('div');
        list.className = 'address-suggest__list';
        list.hidden = true;
        wrap.appendChild(list);

        var status = document.createElement('div');
        status.className = 'address-suggest__status faint';
        status.style.fontSize = '12px';
        status.style.marginTop = '4px';
        wrap.appendChild(status);

        var debounceTimer = null;
        var latestSeq = 0;

        function clearList() {
            list.innerHTML = '';
            list.hidden = true;
        }

        function clearCoords() {
            if (latEl) latEl.value = '';
            if (lonEl) lonEl.value = '';
        }

        async function fetchSuggestions(query) {
            var seq = ++latestSeq;
            try {
                var res = await fetch('/api/address/suggest?q=' + encodeURIComponent(query),
                    { credentials: 'same-origin' });
                if (seq !== latestSeq) return;
                if (!res.ok) {
                    status.textContent = res.status === 403
                        ? 'Подсказки доступны только владельцу.'
                        : 'Не удалось получить подсказки.';
                    clearList();
                    return;
                }
                var items = await res.json();
                renderList(items);
                status.textContent = '';
            } catch (e) {
                if (seq !== latestSeq) return;
                status.textContent = 'Сеть недоступна — подсказки не пришли.';
                clearList();
            }
        }

        function renderList(items) {
            list.innerHTML = '';
            if (!items || items.length === 0) {
                list.hidden = true;
                return;
            }
            items.forEach(function (item) {
                var btn = document.createElement('button');
                btn.type = 'button';
                btn.className = 'address-suggest__item';
                btn.textContent = item.value;
                btn.addEventListener('mousedown', function (ev) {
                    ev.preventDefault();
                    input.value = item.value;
                    if (latEl) latEl.value = (item.latitude != null ? item.latitude : '');
                    if (lonEl) lonEl.value = (item.longitude != null ? item.longitude : '');
                    clearList();
                    status.textContent = (item.latitude && item.longitude)
                        ? 'Координаты получены: ' + item.latitude.toFixed(5) + ', ' + item.longitude.toFixed(5)
                        : 'Адрес выбран (без координат).';
                });
                list.appendChild(btn);
            });
            list.hidden = false;
        }

        input.addEventListener('input', function () {
            clearCoords();
            var q = input.value.trim();
            if (debounceTimer) clearTimeout(debounceTimer);
            if (q.length < 3) {
                clearList();
                status.textContent = '';
                return;
            }
            debounceTimer = setTimeout(function () { fetchSuggestions(q); }, 250);
        });

        input.addEventListener('blur', function () {
            // small delay so click on suggestion still fires
            setTimeout(clearList, 150);
        });

        input.addEventListener('focus', function () {
            if (input.value.trim().length >= 3 && list.children.length > 0) {
                list.hidden = false;
            }
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('input[data-address-suggest]').forEach(initInput);
    });
})();
