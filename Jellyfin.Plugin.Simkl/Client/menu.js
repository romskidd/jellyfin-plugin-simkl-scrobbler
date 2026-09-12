// Adds an "RK Simkl Scrobbler" entry to the user's menu, leading to the
// self-service page. Loaded from index.html by the plugin (WebClientInjection).
// Nothing here relies on the web client's own code: it only looks for the two
// places a user opens their menu from and inserts one link there, once.
(function () {
    'use strict';

    if (window.rkSimklMenu) {
        return;
    }

    window.rkSimklMenu = true;

    var LABEL = 'RK Simkl Scrobbler';
    var ID = 'rkSimklMenuEntry';

    // The script's own address says where the server lives, base url included.
    var script = document.currentScript;
    var match = script && script.src ? /^(.*)\/Simkl\/Client\/menu\.js/i.exec(script.src) : null;
    var href = (match ? match[1] : '') + '/Simkl/Link';

    // Jellyfin 12: the avatar menu at the top right. Its "Settings" item is
    // cloned, so the entry keeps whatever styling the menu currently uses.
    function addToUserMenu() {
        var menu = document.getElementById('app-user-menu');
        if (!menu || menu.querySelector('#' + ID)) {
            return;
        }

        var settings = menu.querySelector('a[href="#/mypreferencesmenu"]');
        if (!settings) {
            return;
        }

        var link = settings.cloneNode(true);
        link.id = ID;
        link.href = href;
        var icon = link.querySelector('svg');
        if (icon) {
            var span = document.createElement('span');
            span.className = 'material-icons';
            span.setAttribute('aria-hidden', 'true');
            span.textContent = 'sync';
            icon.replaceWith(span);
        }

        var text = link.querySelector('.MuiListItemText-primary');
        if (text) {
            text.textContent = LABEL;
        } else {
            link.textContent = LABEL;
        }

        settings.insertAdjacentElement('afterend', link);
    }

    // Jellyfin 10.11, and the classic layout of 12: the side drawer, right
    // after the user's own options.
    function addToDrawer() {
        var container = document.querySelector('.mainDrawer-scrollContainer');
        if (!container || container.querySelector('#' + ID + 'Drawer')) {
            return;
        }

        var userOptions = container.querySelector('.userMenuOptions');
        if (!userOptions) {
            return;
        }

        var section = document.createElement('div');
        section.className = 'rkSimklMenuOptions';
        var header = document.createElement('h3');
        header.className = 'sidebarHeader';
        header.textContent = 'Simkl';
        var link = document.createElement('a');
        link.setAttribute('is', 'emby-linkbutton');
        link.className = 'lnkMediaFolder navMenuOption emby-button';
        link.id = ID + 'Drawer';
        link.href = href;
        link.innerHTML = '<span class="material-icons navMenuOptionIcon" aria-hidden="true">sync</span>'
            + '<span class="sectionName navMenuOptionText"></span>';
        link.querySelector('.navMenuOptionText').textContent = LABEL;
        section.appendChild(header);
        section.appendChild(link);
        userOptions.insertAdjacentElement('afterend', section);
    }

    var scheduled = false;

    function run() {
        scheduled = false;
        try {
            addToUserMenu();
            addToDrawer();
        } catch (e) {
            // Never let this break the web client.
        }
    }

    // Menus are built on demand: watch for new nodes, one pass per tick at most.
    // A timer rather than requestAnimationFrame: frames stop in a background
    // tab, and a pass scheduled there would otherwise never run.
    function schedule() {
        if (!scheduled) {
            scheduled = true;
            window.setTimeout(run, 0);
        }
    }

    function start() {
        run();
        new MutationObserver(function (mutations) {
            for (var i = 0; i < mutations.length; i++) {
                if (mutations[i].addedNodes.length) {
                    schedule();
                    return;
                }
            }
        }).observe(document.body, { childList: true, subtree: true });
    }

    if (document.body) {
        start();
    } else {
        document.addEventListener('DOMContentLoaded', start);
    }
})();
