function fatalPageError(message) {
    let $div = $('<div class="card mt-2" />');
    $('.container').html($div);

    let $div2 = $('<div class="row" />');
    $div.html($div2);

    $div = $div2;
    $div2 = $('<div class="col text-center" />');
    $div2.text(message);
    $div.html($div2);
}

if (!window.Promise) {
    fatalPageError('Your browser is too old to use this page. Please use a modern browser.');
}

function ajaxCmd(cmd, body) {
    body = body || {};
    body.cmd = cmd;

    return new Promise((resolve, reject) => {
        let xhr = new XMLHttpRequest();
        xhr.open('POST', location.pathname);
        xhr.setRequestHeader('Content-Type', 'application/json');
        xhr.send(JSON.stringify(body));

        xhr.addEventListener('readystatechange', function() {
            if (xhr.readyState != XMLHttpRequest.DONE) {
                return;
            }

            if (xhr.status != 200) {
                return reject(new Error('HTTP error ' + xhr.status));
            }

            let response = JSON.parse(xhr.responseText);
            if (response.error && response.fatal) {
                fatalPageError(response.error);
                return;
            }

            resolve(response);
        });
    });
}

function confirmModal(content, buttonText = 'OK') {
    return new Promise((resolve) => {
        if (!Array.isArray(content)) {
            content = [content];
        }

        let $confirmBody = $('#confirm-modal-body');
        let $confirmBtn = $('#btn-confirmmodalconfirm');
        let $modal = $('#confirmModal');

        $confirmBtn.text(buttonText);

        $confirmBody.html('');
        content.forEach((paragraph) => {
            let $p = $('<p />');
            $p.html(paragraph);
            $confirmBody.append($p);
        });

        let modalHide = () => {
            resolve({confirmed: false});
            $modal.off('hide.bs.modal', modalHide);
            $confirmBtn.off('click', modalConfirm);
        };

        let modalConfirm = () => {
            resolve({confirmed: true});
            $modal.off('hide.bs.modal', modalHide);
            $confirmBtn.off('click', modalConfirm);
            $modal.modal('hide');
        };

        $modal.modal();
        $modal.on('hide.bs.modal', modalHide);
        $confirmBtn.on('click', modalConfirm);
    });
}

function isObjectIdentical(a, b) {
    return serializeObject(a) === serializeObject(b);

    function serializeObject(obj) {
        if (obj === null) {
            return 'NULL';
        }
        
        if (typeof obj == 'undefined') {
            return 'UNDEFINED';
        }

        if (typeof obj == 'object') {
            let keys = Object.keys(obj);
            keys.sort();
            return '{' + keys.map(k => `${k}=${serializeObject(obj[k])}`).join(',') + '}';
        }

        return obj.toString();
    }
}

function $div(className) {
    return $(`<div class="${className}" />`);
}

function textCol($row, colClass, value) {
    let $col = $div(colClass);
    $col.text(value);
    $row.append($col);
}

function pluralize(word, quantity) {
    if (quantity == 1) {
        return word;
    }
    
    let finalLetter = word.substring(word.length - 1);
    return finalLetter == 's' ? word + 'es' : word + 's';
}
