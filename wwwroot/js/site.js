










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
