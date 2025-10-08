export function hasSelector(selector) {
    if (!selector || typeof selector !== "string") {
        return false;
    }

    try {
        return document.querySelector(selector) !== null;
    } catch {
        return false;
    }
}
