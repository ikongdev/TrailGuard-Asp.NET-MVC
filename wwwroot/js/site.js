// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Antiforgery for fetch()-based POSTs. ASP.NET's [ValidateAntiForgeryToken]
// normally reads the token from a hidden form field, but a JSON body or a
// hand-built FormData has no such field. Program.cs configures a HeaderName
// instead, which the antiforgery middleware accepts for either request shape -
// so both helpers below just attach the same header read from the meta tag
// _Layout.cshtml renders on every page. Route every fetch() POST through one
// of these two rather than adding the header call-site by call-site.

function getAntiforgeryToken() {
    var meta = document.querySelector('meta[name="request-verification-token"]');
    return meta ? meta.getAttribute('content') : null;
}

function postJson(url, body) {
    return fetch(url, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': getAntiforgeryToken()
        },
        body: JSON.stringify(body)
    });
}

function postForm(url, formData) {
    return fetch(url, {
        method: 'POST',
        headers: {
            'RequestVerificationToken': getAntiforgeryToken()
        },
        body: formData
    });
}
