// Shut a sheet from .NET.
//
// A sheet is a popover, and a popover opened by popovertarget has no Blazor state to clear: it is
// open because the browser says so. So a choice that should also dismiss it -- a set picked from
// the set sheet -- has to ask the browser.
//
// matches() rather than a try/catch, as in railfold.js: hidePopover() on a closed popover throws.
window.ppSheet = {
    hide(id) {
        const sheet = document.getElementById(id);
        if (sheet && sheet.matches(':popover-open')) sheet.hidePopover();
    }
};
