// Bootstrap can't follow the OS light/dark setting by itself, so this sets data-bs-theme to match.
(() => {
    const dark = matchMedia('(prefers-color-scheme: dark)');
    const apply = () => document.documentElement.setAttribute('data-bs-theme', dark.matches ? 'dark' : 'light');
    apply();
    dark.addEventListener('change', apply);
    // Blazor's enhanced navigation copies <html>'s attributes from the server, which drops the theme.
    document.addEventListener('DOMContentLoaded', () => Blazor.addEventListener('enhancedload', apply));
})();
