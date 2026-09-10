(function () {
  "use strict";

  var bodyEl = document.getElementById("documentPreviewBody");
  if (!bodyEl) return; // Not on a document preview page.

  var noticeEl = document.getElementById("documentTranslationNotice");
  var documentId = bodyEl.getAttribute("data-document-id");
  var originalContent = bodyEl.getAttribute("data-original-content") || bodyEl.textContent;
  var cache = {}; // lang -> { content, isTranslated, disclaimer }
  var inFlight = false;

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
    if (!documentId || inFlight) return;

    if (cache[lang]) {
      if (cache[lang].isOriginal) showOriginal();
      else showTranslated(cache[lang]);
      return;
    }

    inFlight = true;
    fetch("/Document/" + encodeURIComponent(documentId) + "/Translate?lang=" + encodeURIComponent(lang), {
      method: "POST"
    })
      .then(function (res) {
        if (!res.ok) throw new Error("Translation request failed");
        return res.json();
      })
      .then(function (data) {
        if (data.isTranslated === false) {
          cache[lang] = { isOriginal: true };
          showOriginal();
        } else {
          cache[lang] = data;
          showTranslated(data);
        }
      })
      .catch(function () {
        showError();
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
