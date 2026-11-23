// wwwroot/js/site.js
// Handles QRDER light/dark theme persistence and responsive sidebar toggling for narrow screens.
(() => {
const root = document.documentElement;
const toggle = document.getElementById("themeToggle");
const storageKey = "qrderTheme";
const legacyKey = "beanCrateTheme";

const savedTheme = localStorage.getItem(storageKey) || localStorage.getItem(legacyKey);
const isCompactButton = document.body?.classList.contains("qrder-mobile-body");

function applyTheme(theme) {
    root.setAttribute("data-bs-theme", theme);
    root.setAttribute("data-theme", theme);

    if (toggle) {
        if (isCompactButton) {
            toggle.textContent = theme === "dark" ? "☀️" : "🌙";
        } else {
            toggle.textContent = theme === "dark" ? "☀️ Light Mode" : "🌙 Dark Mode";
        }

        toggle.setAttribute(
            "aria-label",
            theme === "dark" ? "Switch to light mode" : "Switch to dark mode"
        );
    }
}

applyTheme(savedTheme === "dark" ? "dark" : "light");

toggle?.addEventListener("click", () => {
    const nextTheme = root.getAttribute("data-theme") === "dark" ? "light" : "dark";
    localStorage.setItem(storageKey, nextTheme);
    applyTheme(nextTheme);
});

// Responsive sidebar drawer controls for Owner/Staff viewports < 992px
const sidebar = document.getElementById("qrderSidebar");
const sidebarToggle = document.getElementById("sidebarToggle");
const sidebarBackdrop = document.getElementById("sidebarBackdrop");

function closeSidebar() {
    sidebar?.classList.remove("open");
    sidebarBackdrop?.classList.remove("show");
}

sidebarToggle?.addEventListener("click", () => {
    sidebar?.classList.toggle("open");
    sidebarBackdrop?.classList.toggle("show");
});

sidebarBackdrop?.addEventListener("click", closeSidebar);


})();