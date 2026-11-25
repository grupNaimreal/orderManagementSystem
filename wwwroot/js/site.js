// wwwroot/js/site.js
// Client-side shell behavior for QRDER (Bean Crate Cafe):
// 1. Enforces Light Mode on all customer pages (.qrder-mobile-body) while persisting
//    Light/Dark Mode preferences for Owner/Employee POS users (.qrder-pos-body).
// 2. Controls the responsive slide-over POS sidebar and backdrop on narrow screens.
// 3. Displays the branded telemetry loading curtain on POST forms, Maya redirects,
//    and [data-qrder-loading] links, clearing cleanly on browser pageshow.

(() => {
    const htmlEl = document.documentElement;
    const isCustomerShell = document.body.classList.contains("qrder-mobile-body");
    const themeToggle = document.getElementById("themeToggle");
    const themeStorageKey = "qrder_pos_theme";

    function applyTheme(theme) {
        const normalized = theme === "dark" ? "dark" : "light";
        htmlEl.setAttribute("data-bs-theme", normalized);
        htmlEl.setAttribute("data-theme", normalized);

        if (themeToggle) {
            themeToggle.innerHTML = normalized === "dark"
                ? "&#9728;&#65039; Light Mode"
                : "&#127769; Dark Mode";
            themeToggle.setAttribute("aria-pressed", normalized === "dark" ? "true" : "false");
        }
    }

    // Lock customer/public pages to Light Mode even if Staff previously toggled Dark Mode
    if (isCustomerShell) {
        applyTheme("light");
    } else {
        let savedTheme = "light";
        try {
            savedTheme = localStorage.getItem(themeStorageKey) || "light";
        } catch {
            savedTheme = "light";
        }
        applyTheme(savedTheme);

        themeToggle?.addEventListener("click", () => {
            const current = htmlEl.getAttribute("data-theme") === "dark" ? "dark" : "light";
            const next = current === "dark" ? "light" : "dark";
            applyTheme(next);
            try {
                localStorage.setItem(themeStorageKey, next);
            } catch {
                // Ignore storage restrictions in private browsing modes
            }
        });
    }

    // Staff POS responsive sidebar drawer controls
    const sidebar = document.getElementById("qrderSidebar");
    const sidebarBackdrop = document.getElementById("sidebarBackdrop");
    const sidebarToggle = document.getElementById("sidebarToggle");

    function closeSidebar() {
        sidebar?.classList.remove("open");
        sidebarBackdrop?.classList.remove("show");
    }

    sidebarToggle?.addEventListener("click", () => {
        sidebar?.classList.toggle("open");
        sidebarBackdrop?.classList.toggle("show");
    });

    sidebarBackdrop?.addEventListener("click", closeSidebar);

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape") {
            closeSidebar();
        }
    });

    window.addEventListener("resize", () => {
        if (window.innerWidth >= 768) {
            closeSidebar();
        }
    });

    // Branded telemetry loading overlay for POST submissions & external payment redirects
    const loadingOverlay = document.getElementById("qrderLoadingOverlay");
    const loadingText = document.getElementById("qrderLoadingText");

    window.showQrderLoading = (message) => {
        if (!loadingOverlay) return;
        if (loadingText && message) {
            loadingText.textContent = message;
        }
        loadingOverlay.classList.remove("d-none");
        loadingOverlay.setAttribute("aria-hidden", "false");
    };

    window.hideQrderLoading = () => {
        if (!loadingOverlay) return;
        loadingOverlay.classList.add("d-none");
        loadingOverlay.setAttribute("aria-hidden", "true");
    };

    // Hide overlay if the user navigates back via browser bfcache (e.g., returning from Maya)
    window.addEventListener("pageshow", () => {
        window.hideQrderLoading();
    });

    // Optional link-level loading curtain (data-qrder-loading="Loading message...")
    document.addEventListener("click", (event) => {
        const target = event.target instanceof Element
            ? event.target.closest("[data-qrder-loading]")
            : null;
        if (!target) return;
        if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey) return;

        const msg = target.getAttribute("data-qrder-loading") || "Loading...";
        window.showQrderLoading(msg);
    });

    document.addEventListener("submit", (event) => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement)) return;
        if (event.defaultPrevented) return;
        if (form.hasAttribute("data-no-loading")) return;

        const method = (form.getAttribute("method") || "get").toLowerCase();
        if (method !== "post") return;

        // Respect client-side jQuery unobtrusive validation if attached
        if (typeof window.jQuery !== "undefined" && typeof window.jQuery(form).valid === "function") {
            if (!window.jQuery(form).valid()) {
                return;
            }
        }

        const customMsg = form.getAttribute("data-loading-text");
        const payInput = form.querySelector("#customerPaymentMethod");
        const msg = customMsg
            || (payInput && payInput.value === "Maya"
                ? "Connecting to Maya Checkout..."
                : "Submitting your request...");

        window.showQrderLoading(msg);
    });
})();