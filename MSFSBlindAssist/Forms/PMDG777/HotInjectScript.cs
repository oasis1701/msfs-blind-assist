namespace MSFSBlindAssist.Forms.PMDG777
{
    /// <summary>
    /// Temporary hot-injected JS that augments the bridge with a display scanner,
    /// click_by_index, and set_element_value. Used by the Display debug tab only.
    /// Scheduled for removal in Phase 9 of the native rewrite once every panel
    /// has its own DOM mapping.
    /// </summary>
    internal static class HotInjectScript
    {
        public const string Code = @"
(function() {
    _efb._hotInjectVersion = (_efb._hotInjectVersion || 0) + 1;

    _efb.cmdGetDisplayElements = function() {
        try {
            var items = [];
            _efb._displayElements = [];
            var seen = {};

            var walk = function(el, depth) {
                if (!el || depth > 20) return;
                if (el.nodeType !== 1) return;
                var tag = (el.tagName || '').toLowerCase();
                if (tag === 'script' || tag === 'style' || tag === 'link' || tag === 'meta' || tag === 'noscript') return;

                try {
                    var cs = window.getComputedStyle(el);
                    if (cs.display === 'none' || cs.visibility === 'hidden' || cs.opacity === '0') return;
                } catch(e) {}

                var directText = '';
                for (var n = el.firstChild; n; n = n.nextSibling) {
                    if (n.nodeType === 3) directText += n.textContent;
                }
                directText = directText.trim();

                var label = el.getAttribute('aria-label') || el.getAttribute('title') || el.getAttribute('alt') || el.getAttribute('placeholder') || '';
                var text = directText || label;

                if (!text && el.id) {
                    var idMap = {
                        'efb_dashboard_button': 'Dashboard',
                        'efb_paperwork_button': 'Plan',
                        'efb_charts_button': 'Charts',
                        'efb_authenticate_button': 'Navigraph Auth',
                        'efb_preferences_button': 'Preferences',
                        'efb_navdata_update_button': 'Navigation Data',
                        'efb_information_button': 'Information',
                        'statusbar_home': 'Home'
                    };
                    text = idMap[el.id] || '';
                    if (!text) {
                        var tooltip = el.querySelector('.tooltip-text');
                        if (tooltip) text = tooltip.textContent.trim();
                    }
                }

                if (el.closest && el.closest('#efb_preferences_labels, #efb_preferences_values')) {
                    var kids = el.children;
                    if (kids) { for (var k = 0; k < kids.length; k++) { walk(kids[k], depth + 1); } }
                    return;
                }

                if (el.className && typeof el.className === 'string' && el.className.indexOf('opt-label') >= 0 && el.className.indexOf('opt-output-label') < 0) {
                    var labelText2 = el.textContent.trim();
                    var parent2 = el.closest ? el.closest('.opt-col') : el.parentElement;
                    if (!parent2) parent2 = el.parentElement;
                    if (labelText2 && parent2) {
                        var sibInput = parent2.querySelector('input[type=""text""], input.opt-input');
                        var sibSelect = parent2.querySelector('.opt-select, .custom-select');
                        var sibOutput = parent2.querySelector('.opt-output');
                        var controlEl = sibInput || sibSelect || sibOutput;
                        if (controlEl && controlEl !== el) {
                            var idx2 = _efb._displayElements.length;
                            _efb._displayElements.push(controlEl);
                            var pItem = { index: idx2, text: labelText2, clickable: true, tag: controlEl.tagName.toLowerCase(), role: '' };
                            if (sibInput) {
                                pItem.controlType = 'text';
                                pItem.controlValue = sibInput.value || '';
                                pItem.controlId = sibInput.id || '';
                            } else if (sibSelect) {
                                pItem.controlType = 'select';
                                var so = sibSelect.querySelector('.selected-option');
                                pItem.controlValue = so ? so.textContent.trim() : '';
                                pItem.controlId = sibSelect.id || '';
                                var sOpts = sibSelect.querySelectorAll('.option');
                                if (sOpts.length > 0) {
                                    pItem.controlOptions = [];
                                    for (var si = 0; si < sOpts.length; si++) pItem.controlOptions.push(sOpts[si].textContent.trim());
                                }
                            } else if (sibOutput) {
                                var outVal = sibOutput.textContent.trim();
                                var unitSpan = parent2.querySelector('.output-unit, [class*=""_unit""]');
                                if (unitSpan) outVal += ' ' + unitSpan.textContent.trim();
                                pItem.text = labelText2 + ': ' + (outVal || '--');
                                pItem.clickable = false;
                                delete pItem.controlType;
                            }
                            var key2 = labelText2 + '|perf|' + (pItem.controlType || '');
                            if (!seen[key2]) { seen[key2] = true; items.push(pItem); }
                            return;
                        }
                    }
                }

                if (el.className && typeof el.className === 'string' && el.className.indexOf('opt-output-label') >= 0) {
                    var outLabelText = el.textContent.trim();
                    if (outLabelText) {
                        var outParent = el.parentElement;
                        var outSibling = outParent ? outParent.querySelector('.opt-output') : null;
                        var outVal2 = outSibling ? outSibling.textContent.trim() : '';
                        var unitSpan2 = outParent ? outParent.querySelector('[class*=""_unit""]') : null;
                        if (unitSpan2) outVal2 += ' ' + unitSpan2.textContent.trim();
                        var oidx = _efb._displayElements.length;
                        _efb._displayElements.push(outSibling || el);
                        var okey = outLabelText + '|perfout';
                        if (!seen[okey]) { seen[okey] = true; items.push({ index: oidx, text: outLabelText + ': ' + (outVal2 || '--'), clickable: false, tag: 'p', role: '' }); }
                        return;
                    }
                }

                if (el.closest && el.closest('.opt-col')) {
                    var ecn = el.className || '';
                    if (typeof ecn === 'string' && (ecn.indexOf('opt-input') >= 0)) {
                        return;
                    }
                    if (typeof ecn === 'string' && ecn.indexOf('opt-select') >= 0) {
                        return;
                    }
                }

                var controlType = '';
                var controlValue = '';
                var controlOptions = null;
                var controlId = el.id || '';

                if (tag === 'input') {
                    var inputType = (el.getAttribute('type') || 'text').toLowerCase();
                    if (inputType === 'text' || inputType === 'password' || inputType === 'email' || inputType === 'number') {
                        controlType = 'text';
                        controlValue = el.value || '';
                        if (!text) {
                            var prev = el.parentElement && el.parentElement.previousElementSibling;
                            if (prev) text = prev.textContent.trim();
                        }
                        if (!text) text = label || el.getAttribute('name') || 'Input';
                    } else if (inputType === 'checkbox') {
                        controlType = 'checkbox';
                        controlValue = el.checked ? 'true' : 'false';
                        if (!text) text = label || el.getAttribute('name') || 'Toggle';
                    } else if (inputType === 'range') {
                        controlType = 'range';
                        controlValue = el.value || '50';
                        if (!text) text = label || 'Slider';
                    }
                }

                if (!controlType && el.className && typeof el.className === 'string' && el.className.indexOf('custom-select') >= 0) {
                    controlType = 'select';
                    var selectedOpt = el.querySelector('.selected-option');
                    controlValue = selectedOpt ? selectedOpt.textContent.trim() : '';
                    var optionEls = el.querySelectorAll('.option');
                    if (optionEls.length > 0) {
                        controlOptions = [];
                        for (var oi = 0; oi < optionEls.length; oi++) {
                            controlOptions.push(optionEls[oi].textContent.trim());
                        }
                    }
                    if (!text) text = label || el.id || 'Select';
                }

                if (controlType && !text) {
                    text = label || controlId || controlType;
                }
                if (!controlType && (tag === 'input' || tag === 'select' || tag === 'textarea')) {
                    var val = el.value || '';
                    var lbl = label || el.getAttribute('name') || tag;
                    text = lbl + (val ? ': ' + val : '');
                }

                var isClickable = (tag === 'button' || tag === 'a'
                    || el.getAttribute('role') === 'button' || el.getAttribute('role') === 'tab'
                    || el.getAttribute('role') === 'link' || el.getAttribute('role') === 'menuitem'
                    || el.onclick != null || el.getAttribute('onclick')
                    || (el.className && typeof el.className === 'string' && (el.className.indexOf('btn') >= 0 || el.className.indexOf('clickable') >= 0 || el.className.indexOf('nav-link') >= 0 || el.className.indexOf('icon') >= 0))
                    || el.getAttribute('tabindex') === '0'
                    || el.style.cursor === 'pointer');

                if (controlType) isClickable = true;

                if ((text && text.length > 0 && text.length < 300) || controlType) {
                    if (!text) text = controlType;
                    var key = text.substring(0, 50) + '|' + depth + '|' + controlType;
                    if (!seen[key]) {
                        seen[key] = true;
                        var idx = _efb._displayElements.length;
                        _efb._displayElements.push(el);
                        var item = {
                            index: idx,
                            text: text.replace(/\n/g, ' ').replace(/\s+/g, ' ').trim().substring(0, 150),
                            clickable: isClickable,
                            tag: tag,
                            role: el.getAttribute('role') || ''
                        };
                        if (controlType) {
                            item.controlType = controlType;
                            item.controlValue = controlValue;
                            item.controlId = controlId;
                            if (controlOptions) item.controlOptions = controlOptions;
                        }
                        items.push(item);
                    }
                }

                var kids2 = el.children;
                if (kids2) {
                    for (var k = 0; k < kids2.length; k++) {
                        walk(kids2[k], depth + 1);
                    }
                }
            };

            walk(document.body, 0);

            var prefLabels = document.getElementById('efb_preferences_labels');
            var prefValues = document.getElementById('efb_preferences_values');
            if (prefLabels && prefValues) {
                var labelRows = [];
                var valueRows = [];
                for (var li = 0; li < prefLabels.children.length; li++) {
                    if (prefLabels.children[li].classList && prefLabels.children[li].classList.contains('row'))
                        labelRows.push(prefLabels.children[li]);
                }
                for (var vi = 0; vi < prefValues.children.length; vi++) {
                    if (prefValues.children[vi].classList && prefValues.children[vi].classList.contains('row'))
                        valueRows.push(prefValues.children[vi]);
                }
                var count = Math.min(labelRows.length, valueRows.length);
                var unitNames = {
                    'efb_preferences_distance_unit': ['km','nm'],
                    'efb_preferences_altitude_unit': ['m','ft'],
                    'efb_preferences_length_unit': ['m','ft'],
                    'efb_preferences_speed_unit': ['mps','kph'],
                    'efb_preferences_airspeed_unit': ['kph','kts'],
                    'efb_preferences_temperature_unit': ['F','C'],
                    'efb_preferences_pressure_unit': ['hPa','inHg'],
                    'efb_preferences_weight_unit': ['kg','lb']
                };
                for (var pi = 0; pi < count; pi++) {
                    var labelText = labelRows[pi].textContent.trim();
                    if (!labelText) continue;
                    var valRow = valueRows[pi];
                    var input = valRow.querySelector('input[type=""text""]');
                    var checkbox = valRow.querySelector('input[type=""checkbox""]');
                    var range = valRow.querySelector('input[type=""range""]');
                    var customSelect = valRow.querySelector('.custom-select');
                    var btn = valRow.querySelector('button');

                    var idx = _efb._displayElements.length;
                    var item = { index: idx, text: labelText, clickable: true, tag: 'div', role: '' };

                    if (input) {
                        _efb._displayElements.push(input);
                        item.controlType = 'text';
                        item.controlValue = input.value || '';
                        item.controlId = input.id || '';
                    } else if (customSelect) {
                        _efb._displayElements.push(customSelect);
                        var selOpt = customSelect.querySelector('.selected-option');
                        item.controlType = 'select';
                        item.controlValue = selOpt ? selOpt.textContent.trim() : '';
                        item.controlId = customSelect.id || '';
                        var opts = customSelect.querySelectorAll('.option');
                        item.controlOptions = [];
                        for (var opi = 0; opi < opts.length; opi++) {
                            item.controlOptions.push(opts[opi].textContent.trim());
                        }
                    } else if (checkbox) {
                        var cbId = checkbox.id || '';
                        if (unitNames[cbId]) {
                            _efb._displayElements.push(checkbox);
                            item.controlType = 'select';
                            item.controlOptions = unitNames[cbId];
                            item.controlValue = checkbox.checked ? unitNames[cbId][1] : unitNames[cbId][0];
                            item.controlId = cbId;
                        } else {
                            _efb._displayElements.push(checkbox);
                            item.controlType = 'checkbox';
                            item.controlValue = checkbox.checked ? 'true' : 'false';
                            item.controlId = cbId;
                        }
                    } else if (range) {
                        _efb._displayElements.push(range);
                        item.controlType = 'text';
                        item.controlValue = range.value || '50';
                        item.controlId = range.id || '';
                        item.text = labelText + ' (0-100)';
                    } else if (btn) {
                        _efb._displayElements.push(btn);
                        item.text = btn.textContent.trim() || labelText;
                        item.tag = 'button';
                    } else {
                        _efb._displayElements.push(valRow);
                        item.text = labelText + ': ' + valRow.textContent.trim();
                    }
                    items.push(item);
                }
                var saveBtn = document.getElementById('efb_preferences_save_tablet_prefs');
                if (saveBtn) {
                    var sIdx = _efb._displayElements.length;
                    _efb._displayElements.push(saveBtn);
                    items.push({ index: sIdx, text: 'Save Preferences', clickable: true, tag: 'button', role: '' });
                }
            }

            var outputPanels = document.querySelectorAll('.opt-output-panel');
            for (var opi2 = 0; opi2 < outputPanels.length; opi2++) {
                var panel = outputPanels[opi2];
                var rows = panel.children;
                for (var ri = 0; ri < rows.length; ri++) {
                    var row = rows[ri];
                    var outLabel = row.querySelector('.opt-output-label');
                    var outValue = row.querySelector('.opt-output');
                    if (outLabel) {
                        var lt = outLabel.textContent.trim();
                        var vt = outValue ? outValue.textContent.trim() : '';
                        var ut = row.querySelector('[class*=""_unit""]');
                        if (ut) vt += ' ' + ut.textContent.trim();
                        var okey2 = lt + '|output-panel';
                        if (lt && !seen[okey2]) {
                            seen[okey2] = true;
                            var oidx2 = _efb._displayElements.length;
                            _efb._displayElements.push(outValue || outLabel);
                            items.push({ index: oidx2, text: lt + ': ' + (vt || '--'), clickable: false, tag: 'p', role: '' });
                        }
                    }
                }
            }

            _efb.postState('display_elements', { count: items.length, items: JSON.stringify(items) });
        } catch(e) {
            _efb.postState('error', { message: 'DisplayElements: ' + e.message });
        }
    };

    var origHandler2 = _efb.handleCommand;
    _efb.handleCommand = function(command, payload) {
        if (command === 'set_element_value') {
            var idx = parseInt((payload && payload.index) ? payload.index : '-1');
            var val = (payload && payload.value) ? payload.value : '';
            var ctype = (payload && payload.controlType) ? payload.controlType : '';
            if (idx >= 0 && _efb._displayElements && idx < _efb._displayElements.length) {
                try {
                    var el = _efb._displayElements[idx];
                    if (ctype === 'text') {
                        var proto = (el.tagName === 'TEXTAREA')
                            ? window.HTMLTextAreaElement.prototype
                            : window.HTMLInputElement.prototype;
                        var nativeSetter = Object.getOwnPropertyDescriptor(proto, 'value').set;
                        try { el.focus(); } catch(fe) {}
                        if (el._valueTracker && typeof el._valueTracker.setValue === 'function') {
                            el._valueTracker.setValue('');
                        }
                        nativeSetter.call(el, '');
                        el.dispatchEvent(new Event('input', {bubbles: true}));
                        if (el._valueTracker && typeof el._valueTracker.setValue === 'function') {
                            el._valueTracker.setValue('');
                        }
                        nativeSetter.call(el, val);
                        el.dispatchEvent(new Event('input', {bubbles: true}));
                        el.dispatchEvent(new Event('change', {bubbles: true}));
                        try { el.blur(); } catch(be) {}
                    } else if (ctype === 'checkbox') {
                        var wantChecked = (val === 'true');
                        if (el.checked !== wantChecked) { el.click(); }
                    } else if (ctype === 'select') {
                        if (el.tagName === 'INPUT' && el.type === 'checkbox') {
                            var optIdx = parseInt((payload && payload.optionIndex) ? payload.optionIndex : '0');
                            var wantChecked2 = (optIdx > 0);
                            if (el.checked !== wantChecked2) { el.click(); }
                        } else {
                            var options = el.querySelectorAll('.option');
                            for (var oi = 0; oi < options.length; oi++) {
                                if (options[oi].textContent.trim() === val) { options[oi].click(); break; }
                            }
                        }
                    }
                } catch(e) { _efb.postState('error', {message: 'SetValue: ' + e.message}); }
            }
            return;
        }
        if (command === 'click_by_index') {
            var idx2 = parseInt((payload && payload.idx) ? payload.idx : '-1');
            if (idx2 >= 0) {
                try {
                    var allEls = document.body.querySelectorAll('*');
                    if (idx2 < allEls.length) { allEls[idx2].click(); }
                } catch(e) { _efb.postState('error', {message: 'Click: ' + e.message}); }
            }
            return;
        }
        origHandler2.call(_efb, command, payload);
    };

    console.log('[EFB Hot-Inject] v' + _efb._hotInjectVersion + ' - scanner + form controls');
    return 'injected v' + _efb._hotInjectVersion;
})()
";
    }
}
