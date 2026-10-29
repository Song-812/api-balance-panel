function (provider) {
    // OpenAI / MiMo / domestic browser pages. Gemini uses GeminiBalanceDom.js instead.
    if (provider === 'gemini') return '';
    const labels = (provider === 'openai')
        ? ['API credit balance', 'Credit balance', 'Available balance']
        : ['可用余额', '账户余额', '总余额', 'Available balance', 'Account balance', '余额', '现金余额'];
    const number = '-?\\d[\\d,]*(?:\\.\\d+)?';
    const currency = '(?:US\\$|USD|CNY|RMB|[¥￥$])';
    const money = new RegExp('^(?:-?\\s*' + currency + '\\s*' + number +
        '(?:\\s*(?:USD|CNY|元))?|' + number + '\\s*(?:USD|CNY|美元|元))$', 'i');
    const clean = text => text.trim().replace(/\s+/g, ' ');
    const visible = element => {
        const style = getComputedStyle(element);
        return style.display !== 'none' && style.visibility !== 'hidden' && element.getClientRects().length > 0;
    };
    const interactive = element => /^(BUTTON|A|INPUT|SELECT|TEXTAREA)$/.test(element.tagName) ||
        element.querySelector('button,a,input,select,textarea');
    const amountIn = element => {
        if (!element || !visible(element) || interactive(element)) return '';
        const text = clean(element.innerText || '');
        return money.test(text) ? text : '';
    };
    const normalize = amount => {
        if (/^-\s*(?:US\$|USD|CNY|RMB|[¥￥$])/i.test(amount)) {
            amount = amount.replace(/^-\s*((?:US\$|USD|CNY|RMB|[¥￥$]))\s*/i, '$1-');
        }
        return /--/.test(amount) ? '' : amount;
    };
    const elements = Array.from(document.querySelectorAll('span,div,p,h1,h2,h3,h4,h5,dt,label,strong,b'));
    for (const label of labels) {
        const resultLabel = provider === 'openai' ? 'Credit balance' : label;
        const matches = elements.filter(element => visible(element) &&
            element.textContent.trim().replace(/[:：]\s*$/, '').toLowerCase() === label.toLowerCase());
        for (const element of matches) {
            let region = element;
            for (let level = 0; level < 3 && region && region.tagName !== 'BODY'; level++, region = region.parentElement) {
                const text = clean(region.innerText || '');
                const labelOnly = text.replace(/[:：]\s*$/, '').toLowerCase() === label.toLowerCase();
                if (labelOnly) {
                    const valueElement = region.nextElementSibling;
                    const adjacent = amountIn(valueElement);
                    if (adjacent) {
                        if (amountIn(valueElement.nextElementSibling)) break;
                        const amount = normalize(adjacent);
                        if (amount) return resultLabel + ' ' + amount;
                    }
                }
                if (interactive(region) || text.slice(0, label.length).toLowerCase() !== label.toLowerCase()) continue;
                const amount = text.slice(label.length).replace(/^\s*[:：]?\s*/, '');
                if (!money.test(amount)) continue;
                const normalized = normalize(amount);
                if (normalized) return resultLabel + ' ' + normalized;
            }
        }
        // MiMo often renders "现金余额：￥ 27.55" in one node (no separate label element).
        for (const element of elements) {
            if (!visible(element) || interactive(element)) continue;
            const text = clean(element.innerText || '');
            if (text.length <= label.length) continue;
            if (text.slice(0, label.length).toLowerCase() !== label.toLowerCase()) continue;
            const amount = text.slice(label.length).replace(/^\s*[:：]?\s*/, '');
            if (!money.test(amount)) continue;
            const normalized = normalize(amount);
            if (!normalized) continue;
            // Two money values in sibling nodes stay ambiguous.
            if (amountIn(element.nextElementSibling)) continue;
            return resultLabel + ' ' + normalized;
        }
    }
    return '';
}
