let _widgetId = null;
let _token = '';

window.fsdInitTurnstile = function (siteKey) {
    if (_widgetId !== null) return;
    if (!window.turnstile) {
        setTimeout(() => window.fsdInitTurnstile(siteKey), 200);
        return;
    }
    _widgetId = window.turnstile.render('#turnstile-container', {
        sitekey: siteKey,
        callback: function (token) { _token = token; },
        'expired-callback': function () { _token = ''; },
        'error-callback': function () { _token = ''; }
    });
};

window.fsdGetTurnstileToken = function () { return _token; };

window.fsdResetTurnstile = function () {
    _token = '';
    if (window.turnstile && _widgetId !== null) {
        window.turnstile.reset(_widgetId);
        _widgetId = null;
    }
};
