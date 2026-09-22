/* MuktoAin — chat credit top-up (FR-24). Wires the shared _TopUpModal
   partial on any page that renders it (chat home, profile): live credit
   preview, then POST /Payment/TopUp and hand the browser to the gateway's
   checkout page, which returns to /Payment/Result. */
(function () {
    "use strict";

    function el(id) { return document.getElementById(id); }

    // AUD-1: antiforgery token emitted by _Layout.cshtml on every page.
    function csrfToken() {
        var meta = document.querySelector('meta[name="csrf-token"]');
        return meta ? meta.getAttribute("content") : "";
    }

    function curLang() { return window.mktLang ? window.mktLang.get() : "bn"; }
    function bn(n) { try { return Number(n).toLocaleString("bn-BD"); } catch (e) { return String(n); } }

    function bilingual(node, bnText, enText) {
        node.setAttribute("data-bn", bnText);
        node.setAttribute("data-en", enText);
        node.textContent = curLang() === "en" ? enText : bnText;
    }

    function renderIcons() { if (window.lucide) window.lucide.createIcons(); }

    function init() {
        var submit = el("btn-submit-topup");
        var amountInput = el("topup-amount");
        if (!submit || !amountInput) return;

        var price = parseFloat(amountInput.dataset.creditPrice) || 5;
        var min = parseFloat(amountInput.dataset.minAmount) || 50;
        var preview = el("topup-credits");
        var feedback = el("topup-feedback");

        function amountValue() { return parseFloat(amountInput.value); }
        function isValid(a) { return a >= min && a % price === 0; }

        function showError(bnText, enText) {
            if (!feedback) return;
            feedback.style.display = "block";
            feedback.className = "alert alert-error tiny";
            bilingual(feedback, bnText, enText);
        }

        function updatePreview() {
            if (!preview) return;
            var a = amountValue();
            if (isValid(a)) {
                var credits = a / price;
                bilingual(preview, "= " + bn(credits) + "টি চ্যাট ক্রেডিট", "= " + credits + " chat credits");
            } else {
                bilingual(preview,
                    "কমপক্ষে ৳" + bn(min) + ", ৳" + bn(price) + "-এর গুণিতক",
                    "At least ৳" + min + ", in multiples of ৳" + price);
            }
        }
        amountInput.addEventListener("input", updatePreview);
        updatePreview();

        submit.addEventListener("click", async function () {
            var amount = amountValue();
            if (!isValid(amount)) {
                showError("কমপক্ষে ৳" + bn(min) + " এবং ৳" + bn(price) + "-এর গুণিতক লিখুন।",
                    "Enter at least ৳" + min + ", in multiples of ৳" + price + ".");
                return;
            }

            submit.disabled = true;
            submit.innerHTML = '<i data-lucide="loader"></i> প্রসেসিং... / Processing...';
            renderIcons();

            try {
                var res = await fetch("/Payment/TopUp", {
                    method: "POST",
                    headers: { "Content-Type": "application/json", "RequestVerificationToken": csrfToken() },
                    body: JSON.stringify({
                        amount: amount,
                        method: (document.querySelector('input[name="topup-method"]:checked') || {}).value || "card"
                    })
                });
                var data = await res.json();
                if (data.success && data.gatewayUrl) {
                    window.location.href = data.gatewayUrl;
                    return;
                }
                if (feedback) {
                    feedback.style.display = "block";
                    feedback.className = "alert alert-error tiny";
                    feedback.textContent = data.message || "টপ-আপ ব্যর্থ হয়েছে / Top-up failed.";
                }
            } catch (err) {
                showError("সার্ভারের সাথে সংযোগ করা যায়নি।", "Could not connect to server.");
            } finally {
                submit.disabled = false;
                submit.innerHTML = '<i data-lucide="credit-card"></i> <span></span>';
                bilingual(submit.querySelector("span"), "টপ-আপ করুন", "Top Up");
                renderIcons();
            }
        });
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
    else init();
})();
