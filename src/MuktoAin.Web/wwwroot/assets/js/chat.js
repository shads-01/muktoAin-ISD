/* MuktoAin chat home (FR-19/20) — vanilla JS, no frameworks.
   Talks to /Chat/* endpoints. Relies on main.js for: showToast, modal
   .open toggling (data-open-modal/data-close-modal), data-copy, icons. */
(function () {
    "use strict";

    var state = { chatSessionId: 0, asking: false };

    var thread, input, sendBtn, quotaNote, welcome;

    function el(id) { return document.getElementById(id); }
    function bn(n) { try { return Number(n).toLocaleString("bn-BD"); } catch (e) { return String(n); } }
    function scrollBottom() {
        var sc = document.querySelector(".chat-scroll");
        if (sc) sc.scrollTop = sc.scrollHeight;
    }
    function renderIcons() { if (window.lucide) window.lucide.createIcons(); }

    // ---------- rendering ----------

    function userBubble(text) {
        var d = document.createElement("div");
        d.className = "bubble user";
        d.textContent = text;
        thread.appendChild(d);
        scrollBottom();
    }

    function answerCard(data) {
        var wrap = document.createElement("div");
        wrap.className = "answer-card";

        var head = document.createElement("div");
        head.className = "answer-head";
        head.innerHTML = '<h3><i data-lucide="scale"></i> <span data-bn="আপনার অধিকার" data-en="Your rights">আপনার অধিকার</span></h3>';
        wrap.appendChild(head);

        var p = document.createElement("p");
        p.style.fontFamily = "var(--font-doc)";
        p.style.fontSize = "16px";
        p.style.whiteSpace = "pre-wrap";
        p.textContent = data.answer;
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
            c.style.display = "block";
            c.style.marginTop = "6px";
            c.setAttribute("data-bn", "এই প্রশ্নের উত্তর আগে দেওয়া হয়েছিল (ক্যাশ)।");
            c.setAttribute("data-en", "This question was answered before (cached).");
            c.textContent = "এই প্রশ্নের উত্তর আগে দেওয়া হয়েছিল (ক্যাশ)।";
            wrap.appendChild(c);
        }
        if (data.retrievalOnly) {
            var ro = document.createElement("small");
            ro.className = "muted tiny";
            ro.style.display = "block";
            ro.style.marginTop = "6px";
            ro.textContent = "⚙ AI ছাড়া কীওয়ার্ড-অনুসন্ধানের ফলাফল / retrieved without AI";
            wrap.appendChild(ro);
        }

        var disc = document.createElement("small");
        disc.className = "ai-disclaimer";
        disc.textContent = "⚠ " + (data.disclaimer || "সাধারণ আইনি তথ্য, আনুষ্ঠানিক আইনি পরামর্শ নয়।");
        wrap.appendChild(disc);

        thread.appendChild(wrap);
        quickReplies();
        draftSuggestion();
        renderIcons();
        scrollBottom();
    }

    function quickReplies() {
        var qr = document.createElement("div");
        qr.className = "quick-replies";
        [["নথি বানাতে চাই", "draft"], ["আরও প্রশ্ন আছে", "more"], ["না, ধন্যবাদ", "done"]].forEach(function (pair) {
            var b = document.createElement("button");
            b.className = "btn btn-outline btn-sm";
            b.type = "button";
            b.textContent = pair[0];
            b.addEventListener("click", function () {
                if (pair[1] === "draft") openDraftModal();
                else if (pair[1] === "more") input.focus();
                else showToast("ধন্যবাদ! যেকোনো সময় আবার আসুন।");
            });
            qr.appendChild(b);
        });
        thread.appendChild(qr);
    }

    function draftSuggestion() {
        var d = document.createElement("div");
        d.className = "draft-card";
        var head = document.createElement("div");
        head.className = "row";
        head.innerHTML =
            '<div class="item-ico"><i data-lucide="file-text"></i></div>' +
            '<div><b data-bn="এই সমস্যার জন্য দলিল তৈরি করতে পারি" data-en="I can draft a document for this problem">এই সমস্যার জন্য দলিল তৈরি করতে পারি</b><br>' +
            '<span class="muted tiny" data-bn="আইনজীবী-যাচাইকৃত খসড়া — আপনি সম্পাদনা করতে পারবেন" data-en="Lawyer-verified draft — you can edit it">আইনজীবী-যাচাইকৃত খসড়া — আপনি সম্পাদনা করতে পারবেন</span></div>';
        d.appendChild(head);
        var btn = document.createElement("button");
        btn.className = "btn btn-gold btn-block";
        btn.type = "button";
        btn.style.marginTop = "10px";
        btn.innerHTML = '<i data-lucide="sparkles"></i> <span data-bn="নথি তৈরি করুন" data-en="Generate document">নথি তৈরি করুন</span>';
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
        var actions = document.createElement("div");
        actions.className = "row wrap";
        [["/Account/Register", "user-plus", "নিবন্ধন করুন (৩× সীমা)"],
         ["/Search", "search", "আইন খুঁজুন (বিনামূল্যে)"],
         ["/Case/Submit", "edit-3", "ফর্মে জমা দিন"]].forEach(function (l) {
            var a = document.createElement("a");
            a.className = "btn btn-outline btn-sm";
            a.href = l[0];
            a.innerHTML = '<i data-lucide="' + l[1] + '"></i> ' + l[2];
            actions.appendChild(a);
        });

        // Top-up button (FR-24 sandbox stub)
        var topupBtn = document.createElement("button");
        topupBtn.className = "btn btn-gold btn-sm";
        topupBtn.type = "button";
        topupBtn.setAttribute("data-open-modal", "#topup-modal");
        topupBtn.innerHTML = '<i data-lucide="zap"></i> <span data-bn="টপ-আপ করুন (স্যান্ডবক্স)" data-en="Top Up (Sandbox)">টপ-আপ করুন (স্যান্ডবক্স)</span>';
        actions.appendChild(topupBtn);

        d.appendChild(actions);
        thread.appendChild(d);
        renderIcons();
        scrollBottom();
    }

    function openCitation(s) {
        var title = el("cite-title");
        var text = el("cite-text");
        if (title) title.textContent = (s.actTitle || "") +
            (s.sectionNumber ? " — ধারা " + s.sectionNumber : "");
        if (text) text.textContent = s.sectionText || "";
        var modal = el("citation-modal");
        if (modal) modal.classList.add("open");
        renderIcons();
    }

    // ---------- ask ----------

    function ask(question) {
        if (state.asking || !question || !question.trim()) return;
        state.asking = true;
        sendBtn.disabled = true;
        if (welcome) welcome.style.display = "none";
        userBubble(question);
        var dots = typing();

        fetch("/Chat/Ask", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                chatSessionId: state.chatSessionId,
                question: question,
                language: (localStorage.getItem("mkt-lang") || "bn") === "en" ? "en" : "bn"
            })
        })
        .then(function (r) { return r.json(); })
        .then(function (data) {
            dots.remove();
            if (data.tier === "wall") quotaWallCard();
            else answerCard(data);
            updateQuota(data.remainingToday, data.dailyLimit);
        })
        .catch(function () {
            dots.remove();
            userBubble("⚠ সংযোগ সমস্যা — আবার চেষ্টা করুন / Connection error");
        })
        .finally(function () {
            state.asking = false;
            sendBtn.disabled = false;
        });
    }

    function updateQuota(remaining, limit) {
        if (!quotaNote || typeof remaining !== "number") return;
        quotaNote.textContent = "আজ বাকি: " + bn(remaining) + " / " + bn(limit);
        quotaNote.setAttribute("data-bn", "আজ বাকি: " + bn(remaining) + " / " + bn(limit));
        quotaNote.setAttribute("data-en", "Remaining today: " + remaining + " / " + limit);
    }

    // ---------- draft modal ----------

    function openDraftModal() {
        var ti = el("draft-title-input");
        if (ti && !ti.value) {
            var users = thread.querySelectorAll(".bubble.user");
            if (users.length) ti.value = users[users.length - 1].textContent.slice(0, 250);
        }
        var modal = el("draft-modal");
        if (modal) modal.classList.add("open");
        renderIcons();
    }

    function submitDraft() {
        var btn = el("draft-submit");
        btn.disabled = true;
        btn.textContent = "…";
        fetch("/Chat/Commit", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                chatSessionId: state.chatSessionId,
                categoryId: parseInt(el("draft-category").value, 10),
                districtId: parseInt(el("draft-district").value, 10),
                title: el("draft-title-input").value,
                notificationEmail: el("draft-email").value || null,
                isAnonymous: el("draft-anonymous").checked,
                documentType: el("draft-doc-type").value
            })
        })
        .then(function (r) { return r.json(); })
        .then(function (data) {
            if (data.error) {
                showToast("সমস্যা: " + data.error);
                btn.disabled = false;
                btn.textContent = "নথি তৈরি করুন";
                return;
            }
            window.location.href = data.redirectUrl;
        })
        .catch(function () {
            showToast("সংযোগ সমস্যা — আবার চেষ্টা করুন");
            btn.disabled = false;
            btn.textContent = "নথি তৈরি করুন";
        });
    }

    // ---------- recent chats / resume ----------

    function loadRecent() {
        fetch("/Chat/Recent")
        .then(function (r) { return r.json(); })
        .then(function (data) {
            if (!data.chats || !data.chats.length) return;
            var box = el("recent-chats"), row = el("recent-chats-row");
            if (!box || !row) return;
            data.chats.forEach(function (c) {
                var b = document.createElement("button");
                b.className = "chip chip-sm";
                b.type = "button";
                b.textContent = (c.title || "আলোচনা").slice(0, 32) + " (" + bn(c.messageCount) + ")";
                b.addEventListener("click", function () { resume(c.chatSessionId); });
                row.appendChild(b);
            });
            box.hidden = false;
        })
        .catch(function () {});
    }

    function resume(chatSessionId) {
        fetch("/Chat/Messages?id=" + chatSessionId)
        .then(function (r) { return r.json(); })
        .then(function (data) {
            if (!data.messages) return;
            thread.innerHTML = "";
            if (welcome) welcome.style.display = "none";
            state.chatSessionId = data.chatSessionId;
            data.messages.forEach(function (m) {
                if (m.role === "user") {
                    userBubble(m.content);
                } else {
                    var cited = [];
                    if (m.citedJson) {
                        try {
                            cited = JSON.parse(m.citedJson).map(function (c) {
                                return { actTitle: c.actTitle, sectionNumber: c.sectionNumber,
                                         sectionText: "", sectionId: c.sectionId };
                            });
                        } catch (e) { cited = []; }
                    }
                    answerCard({ answer: m.content, citedSections: cited, disclaimer: "",
                                 fromCache: false, retrievalOnly: false });
                }
            });
        })
        .catch(function () { showToast("আলোচনা খোলা যায়নি"); });
    }

    // ---------- init ----------

    function ensureSession() {
        fetch("/Chat/New", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: "{}"
        })
        .then(function (r) { return r.json(); })
        .then(function (d) { state.chatSessionId = d.chatSessionId; })
        .catch(function () {});
    }

    function loadDistricts() {
        fetch("/Case/SubmitOptions")
        .then(function (r) { return r.json(); })
        .then(function (data) {
            var sel = el("draft-district");
            if (!sel || !data) return;
            sel.innerHTML = "";
            (data.districts || []).forEach(function (d) {
                var o = document.createElement("option");
                o.value = d.id;
                o.textContent = d.name;
                sel.appendChild(o);
            });
        })
        .catch(function () {});
    }

    document.addEventListener("DOMContentLoaded", function () {
        thread = el("chat-thread");
        input = el("chat-input");
        sendBtn = el("chat-send");
        quotaNote = el("quota-note");
        welcome = el("chat-welcome");
        if (!thread || !input || !sendBtn) return;

        // category chips prefill the composer
        document.querySelectorAll("[data-prefill]").forEach(function (chip) {
            if (chip.tagName === "BUTTON") {
                chip.addEventListener("click", function () {
                    input.value = chip.getAttribute("data-prefill");
                    input.focus();
                });
            }
        });

        // mode chips (search mode routes the question through the same Ask
        // endpoint; the marker-free prompt already favors section retrieval)
        document.querySelectorAll("#composer-mode .chip").forEach(function (chip) {
            chip.addEventListener("click", function () {
                if ((chip.dataset.mode || "") === "search") {
                    showToast("ধারা খুঁজুন মোড: প্রশ্ন লিখুন — সরাসরি ধারা দেখানো হবে");
                }
            });
        });

        sendBtn.addEventListener("click", function () {
            ask(input.value);
            input.value = "";
        });
        input.addEventListener("keydown", function (e) {
            if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                ask(input.value);
                input.value = "";
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
                        headers: { "Content-Type": "application/json" },
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
                    renderIcons();
                }
            });
        }

        // deep-link prefill (?prefill= from categories/search)
        var shell = document.querySelector(".chat-shell");
        var pf = shell ? (shell.dataset.prefill || "") : "";
        if (pf) { input.value = decodeURIComponent(pf); input.focus(); }

        ensureSession();
        loadRecent();
        loadDistricts();

        fetch("/Chat/Quota")
        .then(function (r) { return r.json(); })
        .then(function (d) { updateQuota(d.remainingToday, d.dailyLimit); })
        .catch(function () {});
    });
})();
