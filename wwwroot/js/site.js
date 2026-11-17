// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
(() => {
    const root = document.documentElement;
    const toggle = document.getElementById("themeToggle");
    const savedTheme = localStorage.getItem("beanCrateTheme");

    function applyTheme(theme) {
        root.setAttribute("data-bs-theme", theme);
        root.setAttribute("data-theme", theme);

        if (toggle) {
            toggle.textContent = theme === "dark" ? "☀️ Light Mode" : "🌙 Dark Mode";
            toggle.setAttribute(
                "aria-label",
                theme === "dark" ? "Switch to light mode" : "Switch to dark mode"
            );
        }
    }

    applyTheme(savedTheme === "dark" ? "dark" : "light");

    toggle?.addEventListener("click", () => {
        const nextTheme =
            root.getAttribute("data-theme") === "dark" ? "light" : "dark";

        localStorage.setItem("beanCrateTheme", nextTheme);
        applyTheme(nextTheme);
    });
})();