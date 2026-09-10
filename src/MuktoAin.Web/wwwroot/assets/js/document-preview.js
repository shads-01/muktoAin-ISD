(function () {
  "use strict";

  var bodyEl = document.getElementById("documentPreviewBody");
  if (!bodyEl) return; // Not on a document preview page.

  var noticeEl = document.getElementById("documentTranslationNotice");
  var documentId = bodyEl.getAttribute("data-document-id");
  var originalContent = bodyEl.getAttribute("data-original-content") || bodyEl.textContent;
  var cache = {}; // lang -> { content, isTranslated, disclaimer }
  var inFlight = false;
  var latestRequestedLang = null;

  // Detect the document's source language by checking for Bengali Unicode characters
  var documentLanguage = /[ঀ-৿]/.test(originalContent) ? "bn" : "en";

  function showOriginal() {
    bodyEl.textContent = originalContent;
    if (noticeEl) noticeEl.style.display = "none";
  }

  function showTranslated(result) {
    bodyEl.textContent = result.content;
    if (noticeEl) {
      if (result.isTranslated && result.disclaimer) {
        noticeEl.textContent = result.disclaimer;
        noticeEl.style.display = "block";
      } else {
        noticeEl.style.display = "none";
      }
    }
  }

  function showError() {
    bodyEl.textContent = originalContent;
    if (noticeEl) {
      noticeEl.textContent = "Translation unavailable right now — showing the original. / অনুবাদ সাময়িকভাবে অনুপলব্ধ — মূল লেখা দেখানো হচ্ছে।";
      noticeEl.style.display = "block";
    }
  }

  function applyDocumentLanguage(lang) {
    if (!documentId) return;

    // Record this as the latest requested language — all paths (shortcut, cache, fetch) must update this
    // so stale fetch responses can correctly detect they've been superseded
    latestRequestedLang = lang;

    // Finding 1: If the requested language is the document's source language, show original immediately (no network call)
    if (lang === documentLanguage) {
      showOriginal();
      return;
    }

    // Finding 3: Check cache before inFlight gate — cached translations should never be blocked by unrelated in-flight requests
    if (cache[lang]) {
      showTranslated(cache[lang]);
      return;
    }

    // If another fetch is already in flight, don't start a new one
    if (inFlight) return;
    inFlight = true;
    fetch("/Document/" + encodeURIComponent(documentId) + "/Translate?lang=" + encodeURIComponent(lang), {
      method: "POST"
    })
      .then(function (res) {
        if (!res.ok) throw new Error("Translation request failed");
        return res.json();
      })
      .then(function (data) {
        // Finding 3: Only apply this response if it's still the latest requested language
        if (latestRequestedLang === lang) {
          if (data.isTranslated === false) {
            cache[lang] = { isOriginal: true };
            showOriginal();
          } else {
            cache[lang] = data;
            showTranslated(data);
          }
        }
      })
      .catch(function () {
        // Finding 3: Only show error if this was the latest requested language
        if (latestRequestedLang === lang) {
          showError();
        }
      })
      .finally(function () {
        inFlight = false;
      });
  }

  window.addEventListener("languagechange", function (ev) {
    var lang = ev && ev.detail && ev.detail.lang;
    if (lang === "bn" || lang === "en") applyDocumentLanguage(lang);
  });
})();
