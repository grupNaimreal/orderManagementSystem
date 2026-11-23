// wwwroot/js/site.js
// Handles QRDER light/dark theme persistence for Owner/Staff POS pages,
// enforces Light Mode on Customer/Public pages, and toggles the narrow-screen sidebar drawer.
(() => {
const root = document.documentElement;
const toggle = document.getElementById("themeToggle");
const storageKey = "qrderTheme";
const legacyKey = "beanCrateTheme";

const savedTheme = localStorage.getItem(storageKey) || localStorage.getItem(legacyKey);
const isCustomerSide = document.body?.classList.contains("qrder-mobile-body");

function applyTheme(theme) {
    const effectiveTheme = isCustomerSide ? "light" : theme;
    root.setAttribute("data-bs-theme", effectiveTheme);
    root.setAttribute("data-theme", effectiveTheme);

    if (toggle && !isCustomerSide) {
        toggle.textContent = effectiveTheme === "dark" ? "☀️ Light Mode" : "🌙 Dark Mode";
        toggle.setAttribute(
            "aria-label",
            effectiveTheme === "dark" ? "Switch to light mode" : "Switch to dark mode"
        );
    }
}

if (isCustomerSide) {
    applyTheme("light");
} else {
    applyTheme(savedTheme === "dark" ? "dark" : "light");

    toggle?.addEventListener("click", () => {
        const nextTheme = root.getAttribute("data-theme") === "dark" ? "light" : "dark";
        localStorage.setItem(storageKey, nextTheme);
        applyTheme(nextTheme);
    });
}

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

