function () {
    // Gemini / AI Studio billing only. Invoked once per frame by BrowserSession.
    const labels = ['Available credits', 'Available credit', '可用赠金', '可用额度', 'Credit balance', '赠金余额', 'API credit balance', 'Available balance'];
    const number = '-?\\d[\\d,]*(?:\\.\\d+)?';
    const currency = '(?:US\\$|USD|CNY|RMB|[¥￥$])';
    const moneySource = '(?:-?\\s*' + currency + '\\s*' + number +
        '(?:\\s*(?:USD|CNY|元))?|' + number + '\\s*(?:USD|CNY|美元|元))';
    const money = new RegExp('^' + moneySource + '$', 'i');
    const clean = text => (text || '').replace(/\u00a0|\u202f|\u2007/g, ' ').trim().replace(/\s+/g, ' ');
    const visible = element => {
        try {
            const style = getComputedStyle(element);
            return style.display !== 'none' && style.visibility !== 'hidden' && element.getClientRects().length > 0;
        } catch (e) { return false; }
    };
    const interactive = element => /^(BUTTON|A|INPUT|SELECT|TEXTAREA)$/.test(element.tagName) ||
        !!(element.querySelector && element.querySelector('button,a,input,select,textarea'));
    const amountIn = element => {
        if (!element || !visible(element) || interactive(element)) return '';
        const text = clean(element.innerText || element.textContent || '');
        return money.test(text) ? text : '';
    };
    const normalize = amount => {
        if (/^-\s*(?:US\$|USD|CNY|RMB|[¥￥$])/i.test(amount)) {
            amount = amount.replace(/^-\s*((?:US\$|USD|CNY|RMB|[¥￥$]))\s*/i, '$1-');
        }
        return /--/.test(amount) ? '' : amount;
    };
    const escapeRe = value => value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const ownText = element => {
        let text = '';
        for (let i = 0; i < element.childNodes.length; i++) {
            const node = element.childNodes[i];
            if (node.nodeType === 3) text += node.textContent || '';
        }
        return clean(text).replace(/[:：]\s*$/, '');
    };
    const labelOf = element => clean(element.innerText || element.textContent || '').replace(/[:：]\s*$/, '');
    const resultFor = label => (/available\s+credits?/i.test(label) || label === '可用赠金' || label === '可用额度')
        ? 'Available credits' : 'Credit balance';

    const roots = [];
    const queue = [];
    const pushRoot = root => {
        if (!root || roots.indexOf(root) >= 0) return;
        roots.push(root);
        queue.push(root);
    };
    pushRoot(document);
    const elements = [];
    const selector = 'span,div,p,h1,h2,h3,h4,h5,dt,label,strong,b,section,article,li,td,th,small,em';
    while (queue.length) {
        const root = queue.shift();
        let list;
        try { list = root.querySelectorAll ? root.querySelectorAll(selector) : []; } catch (e) { list = []; }
        for (let i = 0; i < list.length; i++) {
            const el = list[i];
            elements.push(el);
            if (el.shadowRoot) pushRoot(el.shadowRoot);
        }
    }

    const takeUniqueAmount = amounts => {
        const unique = [];
        for (let i = 0; i < amounts.length; i++) {
            const amount = normalize(amounts[i]);
            if (!amount) continue;
            if (unique.indexOf(amount) < 0) unique.push(amount);
        }
        return unique.length === 1 ? unique[0] : '';
    };
    const amountsAfter = node => {
        const found = [];
        let sib = node.nextElementSibling;
        while (sib) {
            const direct = amountIn(sib);
            if (direct) found.push(direct);
            else {
                const kids = sib.children || [];
                for (let i = 0; i < kids.length; i++) {
                    const childAmount = amountIn(kids[i]);
                    if (childAmount) found.push(childAmount);
                }
            }
            sib = sib.nextElementSibling;
        }
        return found;
    };

    for (let li = 0; li < labels.length; li++) {
        const label = labels[li];
        const resultLabel = resultFor(label);
        const needle = label.toLowerCase();
        const matches = elements.filter(element => {
            if (!visible(element)) return false;
            return labelOf(element).toLowerCase() === needle || ownText(element).toLowerCase() === needle;
        });
        for (let mi = 0; mi < matches.length; mi++) {
            let region = matches[mi];
            for (let level = 0; level < 5 && region && region.tagName !== 'BODY'; level++, region = region.parentElement) {
                const text = clean(region.innerText || region.textContent || '');
                const labelOnly = text.replace(/[:：]\s*$/, '').toLowerCase() === needle;
                if (labelOnly) {
                    const valueElement = region.nextElementSibling;
                    const adjacent = amountIn(valueElement);
                    if (adjacent) {
                        if (amountIn(valueElement && valueElement.nextElementSibling)) break;
                        const amount = normalize(adjacent);
                        if (amount) return resultLabel + ' ' + amount;
                    }
                    const after = takeUniqueAmount(amountsAfter(region));
                    if (after) return resultLabel + ' ' + after;
                }
                if (!interactive(region)) {
                    const card = text.match(new RegExp('^' + escapeRe(label) + '\\s*[:：]?\\s*(' + moneySource + ')$', 'i'));
                    if (card) {
                        const amount = normalize(card[1]);
                        if (amount) return resultLabel + ' ' + amount;
                    }
                    if (text.slice(0, label.length).toLowerCase() === needle) {
                        const amount = text.slice(label.length).replace(/^\s*[:：]?\s*/, '');
                        if (money.test(amount)) {
                            const normalized = normalize(amount);
                            if (normalized) return resultLabel + ' ' + normalized;
                        }
                    }
                }
                if (level <= 2) {
                    const afterWalk = takeUniqueAmount(amountsAfter(region));
                    if (afterWalk) return resultLabel + ' ' + afterWalk;
                }
            }
        }
    }
    return '';
}