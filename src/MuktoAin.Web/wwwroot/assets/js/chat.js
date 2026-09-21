/* MuktoAin chat home (FR-19/20) — vanilla JS, no frameworks.
   Talks to /Chat/* endpoints. Relies on main.js for: showToast, modal
   .open toggling (data-open-modal/data-close-modal), data-copy, icons. */
(function () {
    "use strict";

    var state = { chatSessionId: 0, asking: false, loading: false, committed: false,
        committing: false, caseFileJson: null, suggestedCategoryId: null, mode: "rights", blocked: false };
    var navigationVersion = 0, historyVersion = 0, historyCursor = null;
    var historyLoading = false, historyFailedCursor = null;
    var desktop = window.matchMedia("(min-width: 900px)");
    var sidebarReturnFocus = null, inertBefore = [], overflowBefore = "";

    async function requestJson(url, options) {
        var response = await fetch(url, options);
        var data;
        try { data = await response.json(); } catch (e) { data = null; }
        if (!response.ok || !data || data.error) {
            var error = new Error(data && data.error ? data.error : "Request failed (" + response.status + ").");
            error.status = response.status;
            throw error;
        }
        return data;
    }

    function postJson(url, body) {
        return requestJson(url, {
            method: "POST",
            headers: { "Content-Type": "application/json", "RequestVerificationToken": csrfToken() },
            body: JSON.stringify(body)
        });
    }

    function active(version, id) {
        return navigationVersion === version && state.chatSessionId === id;
    }

    function markActiveHistory() {
        var sideList = el("chat-history");
        if (!sideList) return;
        sideList.querySelectorAll("[data-chat-id]").forEach(function (link) {
            if (Number(link.dataset.chatId) === state.chatSessionId) link.setAttribute("aria-current", "page");
            else link.removeAttribute("aria-current");
        });
    }

    function resetChat() {
        navigationVersion++;
        state.chatSessionId = 0;
        state.asking = false;
        state.loading = false;
        state.committing = false;
        state.committed = false;
        state.blocked = false;
        state.caseFileJson = null;
        state.suggestedCategoryId = null;
        state.mode = "rights";
        if (thread) thread.replaceChildren();
        if (input) {
            input.value = "";
            input.style.height = "auto";
            input.disabled = false;
            input.removeAttribute("title");
        }
        if (sendBtn) {
            sendBtn.disabled = false;
            sendBtn.removeAttribute("title");
        }
        if (welcome) welcome.style.display = "";
        var intro = el("chat-intro");
        if (intro) intro.hidden = false;
        var compWrap = document.querySelector(".composer-wrap");
        if (compWrap) {
            compWrap.hidden = false;
            compWrap.classList.remove("is-closed");
        }
        var commBox = el("chat-committed");
        if (commBox) {
            commBox.hidden = true;
            commBox.replaceChildren();
        }
        var repStatus = el("chat-replay-status");
        if (repStatus) {
            repStatus.hidden = true;
            repStatus.replaceChildren();
        }
        ["draft-modal", "citation-modal", "topup-modal"].forEach(function (id) {
            var m = el(id);
            if (m) m.classList.remove("open");
        });
        var dEmail = el("draft-email");
        if (dEmail) dEmail.value = "";
        var dAnon = el("draft-anonymous");
        if (dAnon) dAnon.checked = true;
        ["draft-category-label", "draft-district-label", "draft-title-label"].forEach(function (id) {
            var lbl = el(id);
            if (lbl) lbl.textContent = "—";
        });
        var dNote = el("draft-missing-note");
        if (dNote) dNote.hidden = true;
        var dSub = el("draft-submit");
        if (dSub) {
            dSub.disabled = true;
            restoreDraftSubmitLabel(dSub);
        }
        document.querySelectorAll("#composer-mode [data-mode]").forEach(function (chip) {
            chip.classList.toggle("active", chip.dataset.mode === "rights");
        });
        markActiveHistory();
    }

    function setChatBlocked(blocked) {
        state.blocked = !!blocked;
        var compWrap = document.querySelector(".composer-wrap");
        if (state.blocked) {
            if (compWrap) compWrap.classList.add("is-closed");
            var closedText = curLang() === "en" ? "This conversation is closed." : "এই আলোচনাটি বন্ধ করা হয়েছে।";
            if (input) {
                input.disabled = true;
                input.placeholder = closedText;
                input.title = closedText;
            }
            if (sendBtn) {
                sendBtn.disabled = true;
                sendBtn.title = closedText;
            }
            var openEdit = document.querySelector(".user-edit-box");
            if (openEdit) {
                var cancelBtn = openEdit.querySelector(".btn-ghost");
                if (cancelBtn) cancelBtn.click();
                else openEdit.remove();
            }
            var dSug = thread ? thread.querySelector(".draft-card") : null;
            if (dSug) dSug.remove();
            var qR = thread ? thread.querySelector(".quick-replies") : null;
            if (qR) qR.remove();
        } else {
            if (compWrap) compWrap.classList.remove("is-closed");
            if (input) {
                input.removeAttribute("title");
                var defaultText = curLang() === "en" ? "Describe your legal issue..." : "আইনি সমস্যাটি লিখুন...";
                input.placeholder = defaultText;
                if (!state.committed && !state.asking && !state.loading) input.disabled = false;
            }
            if (sendBtn) {
                sendBtn.removeAttribute("title");
                if (!state.committed && !state.asking && !state.loading) sendBtn.disabled = false;
            }
        }
    }

    function replayError(id) {
        var box = el("chat-replay-status");
        if (!box) return;
        box.hidden = false;
        box.replaceChildren();
        box.appendChild(bilingual(document.createElement("p"), "আলোচনা খোলা যায়নি।", "Could not open this conversation."));
        var retry = bilingual(document.createElement("button"), "আবার চেষ্টা করুন", "Retry");
        retry.type = "button";
        retry.className = "btn btn-outline";
        retry.onclick = function () { navigateChat(id, false); };
        box.appendChild(retry);
    }

    function renderCommitted(data) {
        var box = el("chat-committed");
        if (!box) return;
        box.hidden = false;
        box.replaceChildren();
        box.appendChild(bilingual(document.createElement("p"), "এই আলোচনা শুধু পড়ার জন্য।", "This conversation is read-only."));
        if (data.caseUrl) {
            var url = new URL(data.caseUrl, window.location.origin);
            if (url.origin === window.location.origin && url.pathname === "/Case/Result" &&
                url.searchParams.get("id") === String(data.caseId) && !url.searchParams.has("code")) {
                var link = bilingual(document.createElement("a"), "মামলা দেখুন", "View case");
                link.className = "btn btn-outline";
                link.href = url.pathname + url.search;
                box.appendChild(link);
                return;
            }
        }
        box.appendChild(bilingual(document.createElement("p"), "মামলার লিংক পাওয়া যায়নি।", "Case link unavailable."));
    }

    // Envelope draft-type value → DB category id (mirrors ChatService.MapCategory)
    var CATEGORY_BY_DRAFT_TYPE = { LabourComplaint: 1, GeneralDiary: 2, RtiRequest: 3, ConsumerComplaint: 4 };
    var CATEGORY_NAMES = {
        1: { bn: "শ্রম অধিকার ও অভিযোগ", en: "Labour Rights & Complaint" },
        2: { bn: "সাধারণ ডায়েরি (GD)", en: "General Diary (GD)" },
        3: { bn: "তথ্য অধিকার", en: "Right to Information" },
        4: { bn: "ভোক্তা অধিকার", en: "Consumer Rights" }
    };
    function mapCategory(v) { return CATEGORY_BY_DRAFT_TYPE[v] || null; }
    function caseFile() {
        try { return JSON.parse(state.caseFileJson || "{}") || {}; } catch (e) { return {}; }
    }

    var thread, input, sendBtn, quotaNote, welcome;

    function el(id) { return document.getElementById(id); }

    // AUD-1: antiforgery token emitted by _Layout.cshtml on every page.
    function csrfToken() {
        var meta = document.querySelector('meta[name="csrf-token"]');
        return meta ? meta.getAttribute("content") : "";
    }
    function bn(n) { try { return Number(n).toLocaleString("bn-BD"); } catch (e) { return String(n); } }
    function scrollBottom() {
        var sc = document.querySelector(".chat-scroll");
        if (sc) sc.scrollTop = sc.scrollHeight;
    }
    function renderIcons() { if (window.lucide) window.lucide.createIcons(); }
    // AI answers use single newlines between list items rather than strict CommonMark
    // blank-line separation — `breaks` makes marked treat those as intended.
    if (window.marked) window.marked.setOptions({ breaks: true, gfm: true });
    // Current UI language, kept in sync with main.js's toggle (it sets <html lang>).
    function curLang() { return document.documentElement.lang === "en" ? "en" : "bn"; }
    // Sets both data-bn/data-en (so main.js's language-toggle handler can find and
    // flip it later) and the correct initial text for whichever language is active now.
    function bilingual(el, bn, en) {
        el.setAttribute("data-bn", bn);
        el.setAttribute("data-en", en);
        el.textContent = curLang() === "en" ? en : bn;
        return el;
    }

    var LEGAL_DISCLAIMER_EN =
        "⚠️ MuktoAin provides general legal information and document drafting assistance. " +
        "This is NOT formal legal advice. Every document must be reviewed by a verified lawyer " +
        "before use. For urgent legal matters, consult a qualified advocate.";

    var LEGAL_DISCLAIMER_BN =
        "⚠️ মুক্ত আইন সাধারণ আইনি তথ্য ও নথি প্রণয়নে সহায়তা প্রদান করে। এটি আনুষ্ঠানিক আইনি পরামর্শ নয়। " +
        "প্রতিটি নথি ব্যবহারের পূর্বে একজন যাচাইকৃত আইনজীবী দ্বারা পর্যালোচনা করা আবশ্যক।";
    // Blocks below are built via innerHTML strings that hardcode Bangla text
    // alongside data-bn/data-en attributes (relying on main.js's toggle handler
    // to fix them up later). Without this, freshly-inserted nodes show Bangla
    // until the user next clicks a language button. Mirrors main.js's own
    // [data-bn][data-en] sweep (applyLanguage step 2b), scoped to one subtree.
    function applyLangToNode(root) {
        root.querySelectorAll("[data-bn][data-en]").forEach(function (node) {
            var text = curLang() === "en" ? node.dataset.en : node.dataset.bn;
            if (node.children.length === 0) {
                node.textContent = text;
            } else {
                var iconEl = node.querySelector("i, svg");
                node.innerHTML = iconEl ? iconEl.outerHTML + " " + text : text;
            }
        });
        root.querySelectorAll("[data-title-bn][data-title-en]").forEach(function (node) {
            node.title = curLang() === "en" ? node.dataset.titleEn : node.dataset.titleBn;
        });
    }

    function copyText(text, btn, customToast) {
        if (!text) return;
        function done() {
            if (btn) {
                btn.classList.add("copied");
                var origHtml = btn.innerHTML;
                btn.innerHTML = '<i data-lucide="check"></i> <span data-bn="কপি হয়েছে ✓" data-en="Copied ✓">কপি হয়েছে ✓</span>';
                applyLangToNode(btn);
                renderIcons();
                setTimeout(function () {
                    btn.classList.remove("copied");
                    btn.innerHTML = origHtml;
                    applyLangToNode(btn);
                    renderIcons();
                }, 1800);
            }
            var msg = customToast || (curLang() === "en" ? "Copied to clipboard ✓" : "ক্লিপবোর্ডে কপি হয়েছে ✓");
            if (window.showToast) window.showToast(msg);
        }

        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(done, function () {
                fallbackCopy(text, done);
            });
        } else {
            fallbackCopy(text, done);
        }
    }

    function fallbackCopy(text, cb) {
        var ta = document.createElement("textarea");
        ta.value = text;
        ta.style.position = "fixed";
        ta.style.opacity = "0";
        document.body.appendChild(ta);
        ta.select();
        try {
            document.execCommand("copy");
            if (cb) cb();
        } catch (e) {
            if (cb) cb();
        }
        document.body.removeChild(ta);
    }

    function startInlineEdit(wrap, bubbleEl, actionsEl) {
        if (state.asking || state.loading || state.committed || state.committing || state.blocked) return;

        // If another bubble is already being edited, cancel it first
        var existingBox = thread.querySelector(".user-edit-box");
        if (existingBox && existingBox._cancel) {
            existingBox._cancel();
        }

        var origText = bubbleEl.textContent || "";

        bubbleEl.style.display = "none";
        actionsEl.style.display = "none";

        var editBox = document.createElement("div");
        editBox.className = "user-edit-box";

        var textarea = document.createElement("textarea");
        textarea.className = "user-edit-textarea";
        textarea.value = origText;
        textarea.setAttribute("rows", "2");
        textarea.setAttribute("aria-label", curLang() === "en" ? "Edit prompt" : "প্রম্পট সম্পাদনা করুন");

        function autoResize() {
            textarea.style.height = "auto";
            textarea.style.height = Math.min(Math.max(textarea.scrollHeight, 52), 260) + "px";
        }

        textarea.addEventListener("input", autoResize);

        var btnRow = document.createElement("div");
        btnRow.className = "user-edit-actions";

        var cancelBtn = document.createElement("button");
        cancelBtn.className = "btn btn-outline btn-sm";
        cancelBtn.type = "button";
        cancelBtn.innerHTML = '<i data-lucide="x"></i> <span data-bn="বাতিল" data-en="Cancel">বাতিল</span>';
        applyLangToNode(cancelBtn);

        function cancel() {
            editBox.remove();
            bubbleEl.style.display = "";
            actionsEl.style.display = "";
            renderIcons();
        }
        editBox._cancel = cancel;
        cancelBtn.addEventListener("click", cancel);

        var saveBtn = document.createElement("button");
        saveBtn.className = "btn btn-gold btn-sm";
        saveBtn.type = "button";
        saveBtn.innerHTML = '<i data-lucide="send"></i> <span data-bn="পাঠান" data-en="Send">পাঠান</span>';
        applyLangToNode(saveBtn);

        function submit() {
            if (state.asking || state.loading || state.committed || state.committing || state.blocked) return;
            var newText = textarea.value.trim();
            if (!newText) {
                textarea.focus();
                return;
            }

            // Clean up the edit box and restore bubble with updated text
            editBox.remove();
            bubbleEl.textContent = newText;
            wrap._rawText = newText;
            bubbleEl.style.display = "";
            actionsEl.style.display = "";

            // Remove everything in the thread AFTER this prompt bubble
            while (wrap.nextSibling) {
                wrap.nextSibling.remove();
            }

            // Ask the question without appending another user bubble
            ask(newText, { skipUserBubble: true });
        }

        saveBtn.addEventListener("click", submit);

        textarea.addEventListener("keydown", function (e) {
            if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                submit();
            } else if (e.key === "Escape") {
                e.preventDefault();
                cancel();
            }
        });

        btnRow.appendChild(cancelBtn);
        btnRow.appendChild(saveBtn);
        editBox.appendChild(textarea);
        editBox.appendChild(btnRow);

        wrap.insertBefore(editBox, actionsEl);
        renderIcons();

        // Focus & cursor at end
        textarea.focus();
        if (typeof textarea.selectionStart === "number") {
            textarea.selectionStart = textarea.selectionEnd = textarea.value.length;
        }
        autoResize();
    }

    // ---------- rendering ----------

    function userBubble(text) {
        var wrap = document.createElement("div");
        wrap.className = "bubble-wrap user-wrap";
        wrap._rawText = text;

        var d = document.createElement("div");
        d.className = "bubble user";
        d.textContent = text;
        wrap.appendChild(d);

        var actions = document.createElement("div");
        actions.className = "msg-actions user-actions";

        var copyBtn = document.createElement("button");
        copyBtn.className = "msg-action-btn";
        copyBtn.type = "button";
        copyBtn.setAttribute("aria-label", "Copy prompt");
        copyBtn.innerHTML = '<i data-lucide="copy"></i> <span data-bn="কপি" data-en="Copy">কপি</span>';
        applyLangToNode(copyBtn);
        copyBtn.addEventListener("click", function () { copyText(d.textContent, copyBtn); });
        actions.appendChild(copyBtn);

        var editBtn = document.createElement("button");
        editBtn.className = "msg-action-btn";
        editBtn.type = "button";
        editBtn.setAttribute("aria-label", "Edit prompt");
        editBtn.innerHTML = '<i data-lucide="edit-3"></i> <span data-bn="সম্পাদনা" data-en="Edit">সম্পাদনা</span>';
        applyLangToNode(editBtn);
        editBtn.addEventListener("click", function () { startInlineEdit(wrap, d, actions); });
        if (!state.committed && !state.blocked) actions.appendChild(editBtn);

        wrap.appendChild(actions);
        thread.appendChild(wrap);
        renderIcons();
        scrollBottom();
        return wrap;
    }

    // Strip trailing disclaimers from AI prose or historical answer cache
    // because chat UI renders the disclaimer in a dedicated <small class="ai-disclaimer">.
    function stripTrailingDisclaimers(text) {
        if (!text) return "";
        var pattern = /(?:(?:\r?\n|\s)*(?:---|___|\*\*\*)?(?:\r?\n|\s)*(?:⚠️|&warning;|&#9888;)?\s*(?:MuktoAin provides general legal information|মুক্ত\s*আইন\s*সাধারণ\s*আইনি\s*তথ্য|সাধারণ\s*আইনি\s*তথ্য)[\s\S]*?)+$/i;
        return text.replace(pattern, "").trimEnd();
    }

    // Conversational gathering-phase bubble — friendly AI reply, no formal
    // "Your rights" card. Used when readyToExplain=false (no cited sections).
    function conversationalBubble(data, question) {
        var wrap = document.createElement("div");
        wrap.className = "bubble ai";

        var p = document.createElement("div");
        p.className = "ai-reply-text";
        var cleanAnswer = stripTrailingDisclaimers(data.answer || "");
        wrap._rawMarkdown = cleanAnswer || data.answer || "";
        wrap._missingInfo = data.missingInfo || [];
        if (window.marked && window.DOMPurify) {
            p.innerHTML = window.DOMPurify.sanitize(window.marked.parse(cleanAnswer));
        } else {
            p.textContent = cleanAnswer;
        }
        wrap.appendChild(p);

        // Show missingInfo hints so the citizen knows what to provide next
        if (data.missingInfo && data.missingInfo.length) {
            var hint = document.createElement("div");
            hint.className = "missing-info-hint";
            var hintLabel = document.createElement("small");
            hintLabel.className = "muted";
            bilingual(hintLabel,
                "📋 এখনও জানা দরকার: " + data.missingInfo.join(", "),
                "📋 Still needed: " + data.missingInfo.join(", "));
            hint.appendChild(hintLabel);
            wrap.appendChild(hint);
        }

        // Disclaimer sensitive to BN/EN toggle
        var disc = document.createElement("small");
        disc.className = "ai-disclaimer";
        bilingual(disc, LEGAL_DISCLAIMER_BN, LEGAL_DISCLAIMER_EN);
        wrap.appendChild(disc);

        var actions = document.createElement("div");
        actions.className = "msg-actions ai-actions";

        var copyBtn = document.createElement("button");
        copyBtn.className = "msg-action-btn";
        copyBtn.type = "button";
        copyBtn.setAttribute("aria-label", "Copy response");
        copyBtn.innerHTML = '<i data-lucide="copy"></i> <span data-bn="কপি" data-en="Copy">কপি</span>';
        applyLangToNode(copyBtn);
        copyBtn.addEventListener("click", function () { copyText(cleanAnswer || data.answer || "", copyBtn); });
        actions.appendChild(copyBtn);

        var retryBtn = document.createElement("button");
        retryBtn.className = "msg-action-btn";
        retryBtn.type = "button";
        retryBtn.setAttribute("aria-label", "Retry response");
        retryBtn.innerHTML = '<i data-lucide="refresh-cw"></i> <span data-bn="আবার চেষ্টা করুন" data-en="Retry">আবার চেষ্টা করুন</span>';
        applyLangToNode(retryBtn);
        retryBtn.addEventListener("click", function () {
            if (state.asking || state.loading || state.committed || state.committing || state.blocked) return;
            var q = question;
            if (!q) {
                var prev = wrap.previousElementSibling;
                while (prev) {
                    if (prev.classList.contains("user-wrap")) {
                        var b = prev.querySelector(".bubble.user");
                        if (b) { q = b.textContent; break; }
                    }
                    prev = prev.previousElementSibling;
                }
            }
            if (q) {
                while (wrap.nextSibling) wrap.nextSibling.remove();
                wrap.remove();
                ask(q, { skipUserBubble: true });
            }
        });
        if (!state.committed && !state.blocked) actions.appendChild(retryBtn);
        wrap.appendChild(actions);

        thread.appendChild(wrap);
        renderIcons();
        scrollBottom();
    }

    function answerCard(data, question) {
        var wrap = document.createElement("div");
        wrap.className = "answer-card";

        var head = document.createElement("div");
        head.className = "answer-head";
        head.innerHTML = '<h3><span class="avatar"><i data-lucide="scale"></i></span> <span data-bn="আপনার অধিকার" data-en="Your rights">আপনার অধিকার</span></h3>';
        applyLangToNode(head);
        wrap.appendChild(head);

        // A <div>, not a <p>: marked's output is itself block-level (<p>/<ol>/<ul>),
        // which a <p> can't legally contain.
        var p = document.createElement("div");
        p.className = "answer-text";
        var cleanAnswer = stripTrailingDisclaimers(data.answer || "");
        wrap._rawMarkdown = cleanAnswer || data.answer || "";
        wrap._citedSections = data.citedSections || [];
        if (window.marked && window.DOMPurify) {
            p.innerHTML = window.DOMPurify.sanitize(window.marked.parse(cleanAnswer));
        } else {
            p.textContent = cleanAnswer;
        }
        wrap.appendChild(p);

        if (data.citedSections && data.citedSections.length) {
            var chips = document.createElement("div");
            chips.className = "chip-row";
            chips.style.marginTop = "12px";
            data.citedSections.forEach(function (s) {
                var b = document.createElement("button");
                b.className = "citation-chip";
                b.type = "button";
                b.textContent = (s.actTitle || "") +
                    (s.sectionNumber ? " · ধারা " + s.sectionNumber : "");
                b.addEventListener("click", function () { openCitation(s); });
                chips.appendChild(b);
            });
            wrap.appendChild(chips);
        }

        if (data.fromCache) {
            var c = document.createElement("small");
            c.className = "answer-cached tiny";
            bilingual(c, "এই প্রশ্নের উত্তর আগে দেওয়া হয়েছিল (ক্যাশ)।", "This question was answered before (cached).");
            wrap.appendChild(c);
        }
        if (data.retrievalOnly) {
            var ro = document.createElement("small");
            ro.className = "muted tiny";
            bilingual(ro, "⚙ AI ছাড়া কীওয়ার্ড-অনুসন্ধানের ফলাফল", "⚙ Retrieved without AI");
            wrap.appendChild(ro);
        }

        // Disclaimer sensitive to BN/EN toggle
        var disc = document.createElement("small");
        disc.className = "ai-disclaimer";
        bilingual(disc, LEGAL_DISCLAIMER_BN, LEGAL_DISCLAIMER_EN);
        wrap.appendChild(disc);

        var actions = document.createElement("div");
        actions.className = "msg-actions ai-actions";

        var copyBtn = document.createElement("button");
        copyBtn.className = "msg-action-btn";
        copyBtn.type = "button";
        copyBtn.setAttribute("aria-label", "Copy response");
        copyBtn.innerHTML = '<i data-lucide="copy"></i> <span data-bn="কপি" data-en="Copy">কপি</span>';
        applyLangToNode(copyBtn);
        copyBtn.addEventListener("click", function () { copyText(cleanAnswer || data.answer || "", copyBtn); });
        actions.appendChild(copyBtn);

        var retryBtn = document.createElement("button");
        retryBtn.className = "msg-action-btn";
        retryBtn.type = "button";
        retryBtn.setAttribute("aria-label", "Retry response");
        retryBtn.innerHTML = '<i data-lucide="refresh-cw"></i> <span data-bn="আবার চেষ্টা করুন" data-en="Retry">আবার চেষ্টা করুন</span>';
        applyLangToNode(retryBtn);
        retryBtn.addEventListener("click", function () {
            if (state.asking || state.loading || state.committed || state.committing || state.blocked) return;
            var q = question;
            if (!q) {
                var prev = wrap.previousElementSibling;
                while (prev) {
                    if (prev.classList.contains("user-wrap")) {
                        var b = prev.querySelector(".bubble.user");
                        if (b) { q = b.textContent; break; }
                    }
                    prev = prev.previousElementSibling;
                }
            }
            if (q) {
                while (wrap.nextSibling) wrap.nextSibling.remove();
                wrap.remove();
                ask(q, { skipUserBubble: true });
            }
        });
        if (!state.committed && !state.blocked) actions.appendChild(retryBtn);
        wrap.appendChild(actions);

        thread.appendChild(wrap);
        if (!state.blocked) quickReplies(!!data.canDraft);
        if (!state.blocked && data.canDraft) draftSuggestion();
        renderIcons();
        scrollBottom();
    }

    function quickReplies(canDraft) {
        if (state.committed || state.blocked) return;
        var qr = document.createElement("div");
        qr.className = "quick-replies";
        var options = [];
        if (canDraft) {
            options.push(["নথি বানাতে চাই", "I want a document", "draft"]);
        }
        options.push(["আরও প্রশ্ন আছে", "I have more questions", "more"]);
        options.push(["না, ধন্যবাদ", "No, thanks", "done"]);

        options.forEach(function (pair) {
            var b = document.createElement("button");
            b.className = "btn btn-outline btn-sm";
            b.type = "button";
            bilingual(b, pair[0], pair[1]);
            b.addEventListener("click", function () {
                if (pair[2] === "draft") openDraftModal();
                else if (pair[2] === "more") input.focus();
                else showToast(curLang() === "en" ? "Thanks! Come back anytime." : "ধন্যবাদ! যেকোনো সময় আবার আসুন।");
            });
            qr.appendChild(b);
        });
        thread.appendChild(qr);
    }

    function draftSuggestion() {
        if (state.committed || state.blocked) return;
        var existing = thread.querySelector(".draft-card");
        if (existing) existing.remove();

        var d = document.createElement("div");
        d.className = "draft-card";
        var eyebrow = document.createElement("span");
        eyebrow.className = "eyebrow-mini";
        bilingual(eyebrow, "পরবর্তী ধাপ", "Next step");
        d.appendChild(eyebrow);
        var head = document.createElement("div");
        head.className = "row";
        head.innerHTML =
            '<div class="item-ico"><i data-lucide="file-text"></i></div>' +
            '<div><b data-bn="এই সমস্যার জন্য দলিল তৈরি করতে পারি" data-en="I can draft a document for this problem">এই সমস্যার জন্য দলিল তৈরি করতে পারি</b><br>' +
            '<span class="muted tiny" data-bn="আইনজীবী-যাচাইকৃত খসড়া — আপনি সম্পাদনা করতে পারবেন" data-en="Lawyer-verified draft — you can edit it">আইনজীবী-যাচাইকৃত খসড়া — আপনি সম্পাদনা করতে পারবেন</span></div>';
        applyLangToNode(head);
        d.appendChild(head);
        var btn = document.createElement("button");
        btn.className = "btn btn-gold btn-block";
        btn.type = "button";
        btn.style.marginTop = "10px";
        btn.innerHTML = '<i data-lucide="sparkles"></i> <span data-bn="নথি তৈরি করুন" data-en="Generate document">নথি তৈরি করুন</span>';
        applyLangToNode(btn);
        btn.addEventListener("click", openDraftModal);
        d.appendChild(btn);
        thread.appendChild(d);
    }

    function typing() {
        var t = document.createElement("div");
        t.className = "bubble ai typing";
        t.setAttribute("aria-hidden", "true");
        t.innerHTML = "<i></i><i></i><i></i>";
        thread.appendChild(t);
        scrollBottom();
        return t;
    }

    function quotaWallCard() {
        var d = document.createElement("div");
        d.className = "identity-bar";
        d.innerHTML =
            '<div class="item-ico" style="background:var(--primary-soft); color:var(--primary)"><i data-lucide="alarm-clock"></i></div>' +
            '<div style="flex:1"><b data-bn="আজকের AI সীমা শেষ" data-en="Daily AI limit reached">আজকের AI সীমা শেষ</b><br>' +
            '<small class="muted" data-bn="মাঝরাতে (প্রশান্ত মহাসাগরীয়) রিসেট হবে।" data-en="Resets at midnight Pacific.">মাঝরাতে (প্রশান্ত মহাসাগরীয়) রিসেট হবে।</small></div>';
        applyLangToNode(d);
        var actions = document.createElement("div");
        actions.className = "row wrap";
        [["/Account/Register", "user-plus", "নিবন্ধন করুন (৩× সীমা)", "Register (3× limit)"],
         ["/Search", "search", "আইন খুঁজুন (বিনামূল্যে)", "Search laws (free)"],
         ["/Case/Submit", "edit-3", "ফর্মে জমা দিন", "Submit a form"]].forEach(function (l) {
            var a = document.createElement("a");
            a.className = "btn btn-outline btn-sm";
            a.href = l[0];
            var span = document.createElement("span");
            bilingual(span, l[2], l[3]);
            a.innerHTML = '<i data-lucide="' + l[1] + '"></i> ';
            a.appendChild(span);
            actions.appendChild(a);
        });

        // Top-up button (FR-24 sandbox stub)
        var topupBtn = document.createElement("button");
        topupBtn.className = "btn btn-gold btn-sm";
        topupBtn.type = "button";
        topupBtn.setAttribute("data-open-modal", "#topup-modal");
        topupBtn.innerHTML = '<i data-lucide="zap"></i> <span data-bn="টপ-আপ করুন (স্যান্ডবক্স)" data-en="Top Up (Sandbox)">টপ-আপ করুন (স্যান্ডবক্স)</span>';
        applyLangToNode(topupBtn);
        actions.appendChild(topupBtn);

        d.appendChild(actions);
        thread.appendChild(d);
        renderIcons();
        scrollBottom();
    }

    function openCitation(s) {
        var version = navigationVersion;
        var title = el("cite-title");
        var text = el("cite-text");
        if (title) title.textContent = (s.actTitle || "") +
            (s.sectionNumber ? " — ধারা " + s.sectionNumber : "");
        if (text) {
            // A6: replayed messages carry no section text (CitedJson stores
            // only id/title/number) — fetch the authoritative text on open.
            if (s.sectionText) {
                text.textContent = s.sectionText;
            } else if (s.sectionId) {
                text.textContent = "…";
                fetch("/Chat/Citation?id=" + s.sectionId)
                .then(function (r) {
                    if (!r.ok) throw new Error("not found");
                    return r.json();
                })
                .then(function (d) {
                    if (version !== navigationVersion) return;
                    text.textContent = d.sectionText || "";
                })
                .catch(function () {
                    if (version !== navigationVersion) return;
                    text.textContent = curLang() === "en"
                        ? "Section text unavailable."
                        : "ধারার লেখা পাওয়া যায়নি।";
                });
            } else {
                text.textContent = "";
            }
        }
        var modal = el("citation-modal");
        if (modal) modal.classList.add("open");
        renderIcons();
    }

    // ---------- ask ----------

    // A4: distinct AI-style error bubble with a Retry button that resends the
    // failed question. Never rendered as a user bubble (that used to look like
    // the citizen said it).
    function errorBubble(message, retryQuestion) {
        var wrap = document.createElement("div");
        wrap.className = "bubble ai error-bubble";

        var p = document.createElement("div");
        p.className = "ai-reply-text";
        bilingual(p,
            message || "⚠ সংযোগ সমস্যা — উত্তর আনা যায়নি।",
            message || "Connection error — couldn't fetch the answer.");
        wrap.appendChild(p);

        var actions = document.createElement("div");
        actions.className = "msg-actions ai-actions";
        var retryBtn = document.createElement("button");
        retryBtn.className = "msg-action-btn";
        retryBtn.type = "button";
        retryBtn.setAttribute("aria-label", "Retry");
        retryBtn.innerHTML = '<i data-lucide="refresh-cw"></i> <span data-bn="আবার চেষ্টা করুন" data-en="Retry">আবার চেষ্টা করুন</span>';
        applyLangToNode(retryBtn);
        retryBtn.addEventListener("click", function () {
            if (state.asking || state.loading || state.committed || state.committing || state.blocked) return;
            wrap.remove();
            if (retryQuestion) ask(retryQuestion, { skipUserBubble: true });
        });
        if (!state.committed && !state.blocked) actions.appendChild(retryBtn);
        wrap.appendChild(actions);

        thread.appendChild(wrap);
        renderIcons();
        scrollBottom();
    }

    function ask(question, options) {
        options = options || {};
        if (state.asking || state.loading || state.committing || state.committed || state.blocked || !question || !question.trim()) return false;
        var version = navigationVersion;
        var id = state.chatSessionId;
        var mode = state.mode;
        var language = curLang();
        state.asking = true;
        sendBtn.disabled = true;
        el("chat-intro").hidden = true;
        if (!options.skipUserBubble) {
            userBubble(question);
        }
        var dots = typing();
        async function run() {
            try {
                if (!id) {
                    var created = await postJson("/Chat/New", { newChat: true, firstMessage: question });
                    if (!active(version, 0)) return;
                    if (!Number.isInteger(created.chatSessionId) || created.chatSessionId <= 0)
                        throw new Error("Invalid session response.");
                    id = created.chatSessionId;
                    state.chatSessionId = id;
                    window.history.replaceState({ chatSessionId: id }, "", "/Chat?id=" + id);
                    markActiveHistory();
                }
                var data = await postJson("/Chat/Ask", {
                    chatSessionId: id, question: question, language: language, mode: mode
                });
                loadHistory(null);
                if (!active(version, id)) return;
                dots.remove();
                if (data.sessionBlocked === true) {
                    setChatBlocked(true);
                    var activeItem = document.querySelector('.chat-side-item[data-chat-id="' + id + '"]');
                    if (activeItem && !activeItem.querySelector('.chat-side-lock-icon')) {
                        var lock = document.createElement("span");
                        lock.className = "chat-side-lock-icon";
                        lock.setAttribute("data-title-bn", "বন্ধ");
                        lock.setAttribute("data-title-en", "Closed");
                        lock.title = curLang() === "en" ? "Closed" : "বন্ধ";
                        lock.innerHTML = '<i data-lucide="lock"></i>';
                        activeItem.appendChild(lock);
                        renderIcons();
                    }
                }
                state.caseFileJson = data.caseFileJson == null ? null : data.caseFileJson;
                state.suggestedCategoryId = data.suggestedCategoryId == null ? null : data.suggestedCategoryId;
                if (data.tier === "wall") quotaWallCard();
                else if (data.citedSections && data.citedSections.length) answerCard(data, question);
                else conversationalBubble(data, question);
                updateQuota(data.remainingToday, data.dailyLimit);
            } catch (error) {
                if (!active(version, id)) return;
                dots.remove();
                if (error.status === 409) { navigateChat(id, false); return; }
                // A4: a connection failure must not render as if the citizen typed
                // it — show a distinct AI-style error bubble with a Retry button
                // that resends the exact failed question.
                errorBubble(null, question);
            } finally {
                if (active(version, id)) {
                    state.asking = false;
                    sendBtn.disabled = state.committed || state.loading || state.blocked;
                    if (state.blocked) input.disabled = true;
                }
            }
        }
        run();
        return true;
    }

    function updateQuota(remaining, limit) {
        if (!quotaNote || typeof remaining !== "number") return;
        quotaNote.textContent = "আজ বাকি: " + bn(remaining) + " / " + bn(limit);
        quotaNote.setAttribute("data-bn", "আজ বাকি: " + bn(remaining) + " / " + bn(limit));
        quotaNote.setAttribute("data-en", "Remaining today: " + remaining + " / " + limit);
    }

    // ---------- draft confirm card (read-only, prefilled from the AI case file) ----------

    function openDraftModal() {
        if (state.committed || state.loading || state.asking || state.committing || !state.chatSessionId) return;
        var cf = caseFile();
        var catId = state.suggestedCategoryId || mapCategory(cf.category);

        var catEl = el("draft-category-label");
        if (catEl) {
            if (catId && CATEGORY_NAMES[catId]) bilingual(catEl, CATEGORY_NAMES[catId].bn, CATEGORY_NAMES[catId].en);
            else catEl.textContent = "—";
        }
        var dEl = el("draft-district-label");
        if (dEl) dEl.textContent = cf.district || "—";
        var tEl = el("draft-title-label");
        if (tEl) {
            var title = cf.title;
            if (!title) {
                var users = thread.querySelectorAll(".bubble.user");
                if (users.length) title = users[users.length - 1].textContent.slice(0, 250);
            }
            tEl.textContent = title || "—";
        }

        // Missing required field → gate the card; the fix happens in the chat.
        var ready = !!(catId && cf.district);
        var note = el("draft-missing-note");
        if (note) note.hidden = ready;
        var submit = el("draft-submit");
        if (submit) submit.disabled = !ready;

        var modal = el("draft-modal");
        if (modal) modal.classList.add("open");
        renderIcons();
    }

    async function submitDraft() {
        if (state.committed || state.loading || state.asking || state.committing || !state.chatSessionId) return;
        var id = state.chatSessionId, version = navigationVersion, cf = caseFile();
        var btn = el("draft-submit");
        state.committing = true;
        btn.disabled = true;
        btn.textContent = "…";
        try {
            var data = await postJson("/Chat/Commit", {
                chatSessionId: id,
                categoryId: state.suggestedCategoryId || mapCategory(cf.category) || 0,
                districtId: 0,
                title: "",
                notificationEmail: el("draft-email").value || null,
                isAnonymous: el("draft-anonymous").checked,
                language: curLang()
            });
            loadHistory(null);
            if (!active(version, id)) return;
            var target = new URL(data.redirectUrl, window.location.origin);
            if (target.origin !== window.location.origin || target.pathname !== "/Case/Result" ||
                target.searchParams.get("id") !== String(data.caseId) || target.searchParams.has("code"))
                throw new Error("Invalid case link.");
            window.location.href = target.pathname + target.search;
        } catch (error) {
            if (!active(version, id)) return;
            if (window.showToast) window.showToast(curLang() === "en" ? "Could not open the case. Try again." : "মামলা খোলা যায়নি। আবার চেষ্টা করুন।");
        } finally {
            if (active(version, id)) {
                state.committing = false;
                btn.disabled = state.committed;
                restoreDraftSubmitLabel(btn);
            }
        }
    }

    // submitDraft()'s error paths used to overwrite #draft-submit's markup with plain
    // Bangla text, silently undoing the bilingual span the view renders it with.
    function restoreDraftSubmitLabel(btn) {
        btn.innerHTML = '<i data-lucide="sparkles"></i> <span data-bn="নথি তৈরি করুন" data-en="Generate document">' +
            (curLang() === "en" ? "Generate document" : "নথি তৈরি করুন") + '</span>';
        renderIcons();
    }

    // ---------- conversation export (markdown) ----------

    function buildConversationMarkdown() {
        if (!thread || !thread.children || thread.children.length === 0) return "";

        var turns = [];
        var currentTurn = 1;

        for (var i = 0; i < thread.children.length; i++) {
            var child = thread.children[i];

            // Citizen / User message
            if (child._rawText != null) {
                turns.push("### Turn " + currentTurn + " — Citizen\n" + child._rawText.trim());
                continue;
            }

            // Assistant message (conversational or answer-card)
            if (child._rawMarkdown != null) {
                var answerMd = child._rawMarkdown.trim();
                var sectionLines = [];

                if (child._citedSections && child._citedSections.length) {
                    sectionLines.push("\n\n**Citations:**");
                    child._citedSections.forEach(function (s) {
                        var line = "- *" + (s.actTitle || "Act") + "*";
                        if (s.sectionNumber) line += " — ধারা " + s.sectionNumber;
                        sectionLines.push(line);
                    });
                }

                if (child._missingInfo && child._missingInfo.length) {
                    sectionLines.push("\n\n*Still needed info:* " + child._missingInfo.join(", "));
                }

                turns.push("### Turn " + currentTurn + " — Assistant\n" + answerMd + sectionLines.join("\n"));
                currentTurn++;
                continue;
            }

            // Fallback for user wrap if _rawText somehow wasn't set
            if (child.classList && child.classList.contains("user-wrap")) {
                var userTextEl = child.querySelector(".bubble.user");
                if (userTextEl && userTextEl.textContent) {
                    turns.push("### Turn " + currentTurn + " — Citizen\n" + userTextEl.textContent.trim());
                }
                continue;
            }

            // Error bubble
            if (child.classList && child.classList.contains("error-bubble")) {
                var errTextEl = child.querySelector(".ai-reply-text");
                turns.push("### Turn " + currentTurn + " — Assistant (Error)\n" + (errTextEl ? errTextEl.textContent.trim() : "Connection error"));
                currentTurn++;
                continue;
            }
        }

        if (turns.length === 0) return "";

        // Resolve title
        var title = "New Conversation";
        if (state.chatSessionId) {
            var activeHistoryLink = document.querySelector('[data-chat-id="' + state.chatSessionId + '"] span');
            if (activeHistoryLink && activeHistoryLink.textContent) {
                title = activeHistoryLink.textContent.trim();
            } else {
                title = "Chat Session #" + state.chatSessionId;
            }
        }

        var lines = [
            "# MuktoAin Conversation Transcript",
            "- **Session ID**: " + (state.chatSessionId ? state.chatSessionId : "Unsaved"),
            "- **Title**: " + title,
            "- **Status**: " + (state.committed ? "Committed" : "InProgress"),
            "- **Language**: " + curLang(),
            "- **Exported At**: " + new Date().toISOString()
        ];

        if (state.caseFileJson && state.caseFileJson !== "{}" && state.caseFileJson.trim().length > 2) {
            var prettyJson = state.caseFileJson;
            try {
                prettyJson = JSON.stringify(JSON.parse(state.caseFileJson), null, 2);
            } catch (e) {}
            lines.push("\n## Case File Slots (AI Intake)\n```json\n" + prettyJson + "\n```");
        }

        lines.push("\n---\n");
        lines.push(turns.join("\n\n---\n\n"));
        lines.push("\n");

        return lines.join("\n");
    }

    function isMarkdownExportEnabled() {
        var s = document.querySelector(".chat-shell");
        return s ? s.getAttribute("data-enable-md-export") === "true" : false;
    }

    function copyConversationAsMarkdown(btn) {
        if (!isMarkdownExportEnabled()) {
            if (window.console && console.warn) {
                console.warn("Conversation Markdown export is disabled. Enable DevFeatures:EnableConversationMarkdownExport in appsettings.Development.json or set ENABLE_CONVERSATION_MARKDOWN_EXPORT=true.");
            }
            return "";
        }
        var md = buildConversationMarkdown();
        if (!md) {
            var emptyMsg = curLang() === "en" ? "No conversation to copy." : "কপি করার মতো আলোচনা নেই।";
            if (window.showToast) window.showToast(emptyMsg);
            return "";
        }
        var targetBtn = btn || el("chat-copy-md") || el("chat-copy-md-sidebar");
        var toastMsg = curLang() === "en"
            ? "Conversation copied as Markdown ✓"
            : "সম্পূর্ণ আলোচনা MD হিসেবে কপি হয়েছে ✓";
        copyText(md, targetBtn, toastMsg);
        return md;
    }

    // ---------- history sidebar / URL navigation ----------

    var deletedChatIds = {};

    function attachDelete(row, c) {
        var id = Number(c.chatSessionId);
        var btn = document.createElement("button");
        btn.type = "button";
        btn.className = "chat-side-del";
        btn.innerHTML = '<i data-lucide="trash-2"></i>';
        var label = bilingual(document.createElement("span"), "আলোচনা মুছুন", "Delete conversation");
        label.className = "sr-only";
        btn.appendChild(label);
        btn.onclick = function () { confirmDelete(row, btn, c, id); };
        row.appendChild(btn);
    }

    function confirmDelete(row, btn, c, id) {
        var en = curLang() === "en";
        var message = en
            ? "This conversation will be permanently deleted. This cannot be undone."
            : "এই আলোচনা স্থায়ীভাবে মুছে যাবে। এটি আর ফেরানো যাবে না।";
        if (c.status === "Committed")
            message += en ? " Your case will not be deleted, only this conversation."
                : " আপনার মামলা মুছবে না, শুধু এই আলোচনা মুছবে।";
        window.showConfirmDialog({
            title: en ? "Delete this conversation?" : "আলোচনা মুছবেন?",
            message: message,
            okText: en ? "Delete permanently" : "স্থায়ীভাবে মুছুন",
            cancelText: en ? "Cancel" : "বাতিল",
            isDanger: true,
            onCancel: function () { btn.focus(); },
            onConfirm: async function () {
                try {
                    await postJson("/Chat/Delete", { chatSessionId: id });
                } catch (e) {
                    if (e.status !== 404) {
                        if (window.showToast) window.showToast(en ? "Could not delete. Please try again." : "মুছে ফেলা যায়নি। আবার চেষ্টা করুন।", "error");
                        btn.focus();
                        return;
                    }
                }
                removeHistoryRow(row, id);
            }
        });
        var cancel = document.querySelector('#global-confirm-modal .btn[data-confirm-action="cancel"]');
        if (cancel) cancel.focus();
    }

    function removeHistoryRow(row, id) {
        deletedChatIds[id] = true;
        var list = el("chat-history");
        var next = row.nextElementSibling || row.previousElementSibling;
        var focusTarget = next ? next.querySelector("a") : el("chat-new");
        row.remove();
        if (state.chatSessionId === id) {
            window.history.replaceState({ chatSessionId: 0 }, "", "/Chat");
            navigateChat(0, false);
        }
        if (focusTarget && !focusTarget.closest("[inert]")) focusTarget.focus();
        if (!list.children.length) loadHistory(null);
        if (window.showToast) window.showToast(curLang() === "en" ? "Conversation deleted" : "আলোচনা মুছে ফেলা হয়েছে", "success");
    }

    async function loadHistory(cursor) {
        var sideList = el("chat-history");
        if (!sideList) return;
        if (cursor && historyLoading) return;
        var version = ++historyVersion;
        historyLoading = true;
        historyFailedCursor = cursor || null;
        el("chat-history-more").disabled = true;
        el("chat-history-retry").hidden = true;
        bilingual(el("chat-history-status"), "লোড হচ্ছে…", "Loading…");
        var query = cursor ? "?" + new URLSearchParams(cursor).toString() : "";
        try {
            var data = await requestJson("/Chat/Recent" + query);
            if (version !== historyVersion) return;
            var list = el("chat-history");
            if (!cursor) list.replaceChildren();
            data.chats.forEach(function (c) {
                if (deletedChatIds[c.chatSessionId]) return;
                if (list.querySelector('[data-chat-id="' + Number(c.chatSessionId) + '"]')) return;
                var row = document.createElement("div");
                row.className = "chat-side-row";
                var link = document.createElement("a");
                link.className = "chat-side-item";
                link.href = "/Chat?id=" + c.chatSessionId;
                link.dataset.chatId = String(c.chatSessionId);
                var title = document.createElement("span");
                title.className = "chat-side-title";
                title.textContent = c.title;
                link.appendChild(title);
                var meta = document.createElement("span");
                meta.className = "chat-side-meta";
                var date = document.createElement("time");
                date.className = "tiny muted";
                date.dateTime = c.updatedAt;
                var days = Math.max(0, Math.floor((Date.now() - Date.parse(c.updatedAt)) / 86400000));
                bilingual(date, days === 0 ? "আজ" : bn(days) + " দিন আগে", days === 0 ? "Today" : days + " days ago");
                meta.appendChild(date);
                link.appendChild(meta);
                if (c.status === "Committed") {
                    var badge = bilingual(document.createElement("span"), "মামলা", "Case");
                    badge.className = "chat-side-badge";
                    meta.appendChild(badge);
                } else if (c.status === "Blocked" || c.status === "blocked") {
                    var lock = document.createElement("span");
                    lock.className = "chat-side-lock-icon";
                    lock.setAttribute("data-title-bn", "বন্ধ");
                    lock.setAttribute("data-title-en", "Closed");
                    lock.title = curLang() === "en" ? "Closed" : "বন্ধ";
                    lock.innerHTML = '<i data-lucide="lock"></i>';
                    link.appendChild(lock);
                    renderIcons(link);
                }
                link.addEventListener("click", function (event) {
                    if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
                    event.preventDefault();
                    navigateChat(c.chatSessionId, true);
                });
                row.appendChild(link);
                attachDelete(row, c);
                list.appendChild(row);
                renderIcons();
            });
            historyCursor = data.nextCursor;
            el("chat-history-more").hidden = !historyCursor;
            bilingual(el("chat-history-status"), list.children.length ? "" : "কোনো আলোচনা নেই।",
                list.children.length ? "" : "No conversations yet.");
            markActiveHistory();
        } catch (error) {
            if (version !== historyVersion) return;
            bilingual(el("chat-history-status"), "ইতিহাস লোড হয়নি।", "History could not be loaded.");
            el("chat-history-retry").hidden = false;
        } finally {
            if (version === historyVersion) {
                historyLoading = false;
                el("chat-history-more").disabled = false;
            }
        }
    }

    async function navigateChat(id, push) {
        resetChat();
        state.chatSessionId = id;
        var version = navigationVersion;
        var path = id ? "/Chat?id=" + id : "/Chat";
        if (push && window.location.pathname + window.location.search !== path)
            window.history.pushState({ chatSessionId: id }, "", path);
        if (!desktop.matches) setSidebar(false);
        markActiveHistory();
        if (!id) { input.focus(); return; }
        state.loading = true;
        sendBtn.disabled = true;
        input.disabled = true;
        el("chat-intro").hidden = true;
        var dots = typing();
        try {
            var data = await requestJson("/Chat/Messages?id=" + id);
            if (!active(version, id)) return;
            if (data.chatSessionId !== id || !Array.isArray(data.messages)) throw new Error("Invalid replay.");
            dots.remove();
            state.committed = data.committed === true;
            state.blocked = data.blocked === true;
            state.caseFileJson = data.caseFileJson == null ? null : data.caseFileJson;
            state.suggestedCategoryId = data.suggestedCategoryId == null ? null : data.suggestedCategoryId;
            document.querySelector(".composer-wrap").hidden = state.committed;
            setChatBlocked(state.blocked);
            var lastUserContent = "";
            data.messages.forEach(function (m) {
                if (m.role === "user") { lastUserContent = m.content; userBubble(m.content); return; }
                var cited = [];
                try { cited = JSON.parse(m.citedJson || "[]"); } catch (e) { cited = []; }
                if (!Array.isArray(cited)) cited = [];
                var replayData = { answer: m.content, citedSections: cited, disclaimer: "",
                    fromCache: false, retrievalOnly: false, canDraft: false };
                if (cited.length) answerCard(replayData, lastUserContent);
                else conversationalBubble(replayData, lastUserContent);
            });
            if (state.committed) renderCommitted(data);
            else if (!state.blocked && data.canDraft) draftSuggestion();
            state.loading = false;
            input.disabled = state.committed || state.blocked;
            sendBtn.disabled = state.committed || state.blocked;
            markActiveHistory();
        } catch (error) {
            if (!active(version, id)) return;
            dots.remove();
            thread.replaceChildren();
            replayError(id);
        }
    }

    function routeFromLocation() {
        var raw = new URLSearchParams(window.location.search).get("id");
        if (raw == null) { navigateChat(0, false); return; }
        var id = Number(raw);
        if (!/^[1-9]\d*$/.test(raw) || !Number.isSafeInteger(id) || id > 2147483647) {
            resetChat();
            state.loading = true;
            input.disabled = sendBtn.disabled = true;
            el("chat-replay-status").hidden = false;
            bilingual(el("chat-replay-status"), "আলোচনার ঠিকানা সঠিক নয়।", "Invalid conversation address.");
            return;
        }
        navigateChat(id, false);
    }

    // ---------- drawer behavior ----------

    function setSidebar(open) {
        var side = el("chat-side"), toggle = el("chat-side-toggle");
        if (!side || !toggle) return;
        var wasOpen = !side.hidden;
        inertBefore.forEach(function (entry) { entry.node.inert = entry.value; });
        inertBefore = [];
        document.body.style.overflow = overflowBefore;
        side.hidden = !open;
        side.inert = !open;
        toggle.setAttribute("aria-expanded", String(open));
        el("chat-side-backdrop").hidden = !open || desktop.matches;
        side.removeAttribute("role");
        side.removeAttribute("aria-modal");
        if (open && !desktop.matches) {
            if (!wasOpen) sidebarReturnFocus = document.activeElement;
            overflowBefore = document.body.style.overflow;
            document.body.style.overflow = "hidden";
            var node = side;
            while (node.parentElement && node !== document.body) {
                Array.from(node.parentElement.children).forEach(function (sibling) {
                    if (sibling === node || sibling.id === "chat-side-backdrop" || sibling.tagName === "SCRIPT") return;
                    inertBefore.push({ node: sibling, value: sibling.inert });
                    sibling.inert = true;
                });
                node = node.parentElement;
            }
            side.setAttribute("role", "dialog");
            side.setAttribute("aria-modal", "true");
            el("chat-side-close").focus();
        } else if (!open && wasOpen) {
            var focus = sidebarReturnFocus && sidebarReturnFocus.isConnected ? sidebarReturnFocus : toggle;
            if (!focus.inert) focus.focus();
            sidebarReturnFocus = null;
        }
    }

    function initSidebar() {
        var side = el("chat-side");
        if (!side) return;
        el("chat-side-toggle").onclick = function () { setSidebar(el("chat-side").hidden); };
        el("chat-side-close").onclick = function () { setSidebar(false); };
        el("chat-side-backdrop").onclick = function () { setSidebar(false); };
        el("chat-new").onclick = function () { navigateChat(0, true); };
        el("chat-history-more").onclick = function () { if (historyCursor) loadHistory(historyCursor); };
        el("chat-history-retry").onclick = function () { loadHistory(historyFailedCursor); };
        document.addEventListener("keydown", function (event) {
            var sideEl = el("chat-side");
            if (sideEl.hidden || desktop.matches) return;
            if (event.key === "Escape") {
                if (el("global-confirm-modal")) return;
                event.preventDefault(); setSidebar(false); return;
            }
            if (event.key !== "Tab") return;
            var nodes = Array.from(sideEl.querySelectorAll('a[href],button:not([disabled]),[tabindex="0"]'))
                .filter(function (node) { return !node.hidden && node.getClientRects().length > 0; });
            if (!nodes.length) return;
            var first = nodes[0], last = nodes[nodes.length - 1];
            if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
            else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
        });
        desktop.addEventListener("change", function () { setSidebar(desktop.matches); });
        overflowBefore = document.body.style.overflow;
        setSidebar(desktop.matches);
    }

    document.addEventListener("DOMContentLoaded", function () {
        thread = el("chat-thread");
        input = el("chat-input");
        sendBtn = el("chat-send");
        quotaNote = el("quota-note");
        welcome = el("chat-welcome");
        if (!thread || !input || !sendBtn) return;

        // category chips prefill the composer — data-prefill is Bangla, data-prefill-en
        // (when present) is the English variant; pick per the active toggle language.
        document.querySelectorAll("[data-prefill]").forEach(function (chip) {
            if (chip.tagName === "BUTTON") {
                chip.addEventListener("click", function () {
                    if (state.committed || state.loading || state.committing) return;
                    var enPrefill = chip.getAttribute("data-prefill-en");
                    input.value = (curLang() === "en" && enPrefill) ? enPrefill : chip.getAttribute("data-prefill");
                    input.focus();
                });
            }
        });

        // A2: mode chips actually switch behavior — "search" routes the
        // question through keyword section retrieval (FR-7).
        document.querySelectorAll("#composer-mode [data-mode]").forEach(function (chip) {
            chip.addEventListener("click", function () {
                if (state.committed || state.loading || state.committing) return;
                document.querySelectorAll("#composer-mode [data-mode]").forEach(function (c) { c.classList.remove("active"); });
                chip.classList.add("active");
                state.mode = chip.dataset.mode || "rights";
            });
        });

        // A3: clear the composer only when ask() actually accepted the
        // message — a turn in flight must not silently drop what you typed.
        sendBtn.addEventListener("click", function () {
            if (ask(input.value)) input.value = "";
        });
        input.addEventListener("keydown", function (e) {
            if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                if (ask(input.value)) input.value = "";
            }
        });

        var ds = el("draft-submit");
        if (ds) ds.addEventListener("click", submitDraft);

        var topupSubmit = el("btn-submit-topup");
        if (topupSubmit) {
            topupSubmit.addEventListener("click", async function () {
                var amountInput = el("topup-amount");
                var amount = parseFloat(amountInput ? amountInput.value : 0);
                var feedback = el("topup-feedback");
                if (!amount || amount <= 0) {
                    if (feedback) {
                        feedback.style.display = "block";
                        feedback.className = "alert alert-error tiny";
                        feedback.textContent = "অনুগ্রহ করে সঠিক টাকার পরিমাণ লিখুন / Please enter a valid amount.";
                    }
                    return;
                }

                topupSubmit.disabled = true;
                topupSubmit.innerHTML = '<i data-lucide="loader"></i> প্রসেসিং... / Processing...';
                renderIcons();

                try {
                    var res = await fetch("/Payment/TopUp", {
                        method: "POST",
                        headers: { "Content-Type": "application/json", "RequestVerificationToken": csrfToken() },
                        body: JSON.stringify({ amount: amount })
                    });
                    var data = await res.json();
                    if (data.success) {
                        if (window.showToast) {
                            showToast(data.message || "টপ-আপ সফল হয়েছে! / Top-up successful!", "success");
                        }
                        var topupModal = el("topup-modal");
                        if (topupModal) topupModal.classList.remove("open");
                        if (feedback) feedback.style.display = "none";
                    } else {
                        if (feedback) {
                            feedback.style.display = "block";
                            feedback.className = "alert alert-error tiny";
                            feedback.textContent = data.message || "টপ-আপ ব্যর্থ হয়েছে / Top-up failed.";
                        }
                    }
                } catch (err) {
                    if (feedback) {
                        feedback.style.display = "block";
                        feedback.className = "alert alert-error tiny";
                        feedback.textContent = "সার্ভারের সাথে সংযোগ করা যায়নি / Could not connect to server.";
                    }
                } finally {
                    topupSubmit.disabled = false;
                    topupSubmit.innerHTML = '<i data-lucide="credit-card"></i> <span data-bn="টপ-আপ করুন (স্যান্ডবক্স)" data-en="Top Up (Sandbox)">টপ-আপ করুন (স্যান্ডবক্স)</span>';
                    applyLangToNode(topupSubmit);
                    renderIcons();
                }
            });
        }

        initSidebar();
        window.addEventListener("popstate", routeFromLocation);
        routeFromLocation();
        loadHistory(null);

        // deep-link prefill (?prefill= from categories/search) — only when the
        // URL doesn't address a specific conversation; Razor passes it decoded.
        var shell = document.querySelector(".chat-shell");
        var pf = shell ? (shell.dataset.prefill || "") : "";
        if (pf && new URLSearchParams(location.search).get("id") == null) {
            input.value = pf;
            input.focus();
        }

        requestJson("/Chat/Quota").then(function (data) {
            updateQuota(data.remainingToday, data.dailyLimit);
        }).catch(function () {
            bilingual(quotaNote, "কোটা এখন দেখা যাচ্ছে না।", "Quota is temporarily unavailable.");
        });

        // Copy conversation as Markdown (Dev Feature toggle)
        if (isMarkdownExportEnabled()) {
            // Keyboard shortcut (Alt + M / Option + M)
            document.addEventListener("keydown", function (e) {
                if (e.altKey && (e.key === "m" || e.key === "M" || e.code === "KeyM")) {
                    e.preventDefault();
                    copyConversationAsMarkdown();
                }
            });

            // Global console helper for developers
            window.copyChatAsMarkdown = function () {
                var md = copyConversationAsMarkdown();
                if (md && window.console && console.log) {
                    console.log("=== MuktoAin Conversation Markdown ===\n" + md);
                }
                return md;
            };
        } else {
            window.copyChatAsMarkdown = function () {
                var msg = "Conversation Markdown export is disabled. Enable DevFeatures:EnableConversationMarkdownExport in appsettings.Development.json or set ENABLE_CONVERSATION_MARKDOWN_EXPORT=true.";
                if (window.console && console.warn) console.warn(msg);
                return null;
            };
        }

        // Language observer: keep blocked state messages and title tooltips in sync when language toggles
        if (window.MutationObserver) {
            var langObserver = new MutationObserver(function (mutations) {
                mutations.forEach(function (m) {
                    if (m.attributeName === "lang") {
                        applyLangToNode(document);
                        if (state.blocked) {
                            setChatBlocked(true);
                        }
                    }
                });
            });
            langObserver.observe(document.documentElement, { attributes: true, attributeFilter: ["lang"] });
        }
    });
})();
