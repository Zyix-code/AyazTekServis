(() => {
    const body = document.body;
    const printButton = document.querySelector('[data-report-print]');
    const closeButton = document.querySelector('[data-report-close]');
    const printReport = () => window.print();

    printButton?.addEventListener('click', printReport);
    closeButton?.addEventListener('click', () => {
        try {
            if (window.opener && !window.opener.closed) {
                window.close();
                return;
            }
        } catch { }
        if (body.dataset.fromCreate === 'true') {
            window.location.replace('/Service/AddService?fresh=true');
            return;
        }
        window.location.replace(body.dataset.detailUrl || '/Service');
    });
    if (body.dataset.autoPrint === 'true') {
        window.addEventListener('load', () => window.print(), { once: true });
    }
})();
