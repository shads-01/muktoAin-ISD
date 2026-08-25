/* ============================================================
   MuktoAin — shared UI behavior (v2, Parchment Sepia)
   Theme · drawer · popovers · tabs · modals/sheets · toasts
   No frameworks. Progressive enhancement only.
   ============================================================ */
(function () {
  "use strict";

  /* ---------- Theme (light / night library) ---------- */
  var saved = null;
  try { saved = localStorage.getItem("mkt-theme"); } catch (e) {}
  var theme = saved || (matchMedia("(prefers-color-scheme:dark)").matches ? "dark" : "light");
  document.documentElement.dataset.theme = theme;

  function setTheme(t) {
    document.documentElement.dataset.theme = t;
    try { localStorage.setItem("mkt-theme", t); } catch (e) {}
    document.querySelectorAll(".theme-toggle").forEach(function (b) {
      b.innerHTML = icon(t === "dark" ? "sun" : "moon");
    });
    renderIcons();
  }
  function icon(name) {
    return '<i data-lucide="' + name + '"></i>';
  }

  /* ---------- Lucide render helper ---------- */
  function renderIcons(root) {
    if (window.lucide) window.lucide.createIcons(root ? { nameAttr: "data-lucide", attrs: {}, root: root } : undefined);
  }

  document.addEventListener("DOMContentLoaded", function () {
    /* theme toggle buttons */
    document.querySelectorAll(".theme-toggle").forEach(function (b) {
      b.setAttribute("aria-label", b.getAttribute("aria-label") || "Toggle theme");
      if (!b.innerHTML.trim()) b.innerHTML = icon(theme === "dark" ? "sun" : "moon");
      b.addEventListener("click", function () {
        setTheme(document.documentElement.dataset.theme === "dark" ? "light" : "dark");
      });
    });

    /* language toggle (visual only in mockups) */
    document.querySelectorAll(".lang-toggle").forEach(function (group) {
      group.querySelectorAll("button").forEach(function (btn) {
        btn.addEventListener("click", function () {
          group.querySelectorAll("button").forEach(function (x) { x.classList.remove("active"); });
          btn.classList.add("active");
        });
      });
    });

    /* filter chips */
    document.querySelectorAll("[data-chip-group]").forEach(function (group) {
      group.querySelectorAll(".chip").forEach(function (chip) {
        chip.addEventListener("click", function () {
          if (group.hasAttribute("data-chip-multi")) {
            chip.classList.toggle("active");
          } else {
            group.querySelectorAll(".chip").forEach(function (c) { c.classList.remove("active"); });
            chip.classList.add("active");
          }
          var ev = new CustomEvent("chipchange", { detail: chip, bubbles: true });
          group.dispatchEvent(ev);
        });
      });
    });

    /* underline tabs */
    document.querySelectorAll("[data-tabs]").forEach(function (tabsEl) {
      var buttons = tabsEl.querySelectorAll("button");
      buttons.forEach(function (btn) {
        btn.addEventListener("click", function () {
          buttons.forEach(function (b) { b.classList.remove("active"); });
          btn.classList.add("active");
          var scope = document.querySelector(tabsEl.dataset.tabs) || document;
          scope.querySelectorAll(":scope > .tab-panel, :scope .tab-panel").forEach(function (p) {
            p.classList.toggle("active", p.id === btn.dataset.panel);
          });
          renderIcons();
        });
      });
    });

    /* mobile drawer */
    var drawer = document.getElementById("drawer");
    var backdrop = document.getElementById("drawer-backdrop");
    function closeDrawer() {
      if (!drawer) return;
      drawer.classList.remove("open");
      if (backdrop) backdrop.classList.remove("open");
      var burger = document.querySelector(".nav-burger");
      if (burger) burger.setAttribute("aria-expanded", "false");
    }
    document.querySelectorAll(".nav-burger").forEach(function (b) {
      b.addEventListener("click", function () {
        drawer.classList.add("open");
        if (backdrop) backdrop.classList.add("open");
        b.setAttribute("aria-expanded", "true");
      });
    });
    if (backdrop) backdrop.addEventListener("click", closeDrawer);
    document.querySelectorAll("[data-close-drawer]").forEach(function (b) {
      b.addEventListener("click", closeDrawer);
    });
    document.addEventListener("keydown", function (e) {
      if (e.key === "Escape") { closeDrawer(); closeAllModals(); closePops(); }
    });

    /* avatar / popover menus */
    document.querySelectorAll("[data-pop]").forEach(function (trigger) {
      trigger.addEventListener("click", function (e) {
        e.stopPropagation();
        var pop = document.getElementById(trigger.dataset.pop);
        var isOpen = pop && pop.classList.contains("open");
        closePops();
        if (pop && !isOpen) pop.classList.add("open");
      });
    });
    function closePops() {
      document.querySelectorAll(".menu-pop.open").forEach(function (p) { p.classList.remove("open"); });
    }
    document.addEventListener("click", function (e) {
      if (!e.target.closest(".pop-wrap")) closePops();
    });

    /* modals & bottom sheets: [data-open-modal="#id"] / [data-close-modal] */
    document.querySelectorAll("[data-open-modal]").forEach(function (t) {
      t.addEventListener("click", function (e) {
        e.preventDefault();
        var m = document.querySelector(t.dataset.openModal);
        if (m) { m.classList.add("open"); var f = m.querySelector("input,textarea,select,button"); if (f) f.focus({ preventScroll: true }); }
      });
    });
    document.querySelectorAll("[data-close-modal]").forEach(function (t) {
      t.addEventListener("click", function () {
        var bd = t.closest(".modal-backdrop");
        if (bd) bd.classList.remove("open");
      });
    });
    function closeAllModals() {
      document.querySelectorAll(".modal-backdrop.open").forEach(function (m) { m.classList.remove("open"); });
    }

    /* toast: showToast(msg, type) global */
    window.showToast = function (msg, type) {
      var old = document.querySelector(".toast");
      if (old) old.remove();
      var el = document.createElement("div");
      el.className = "toast " + (type || "");
      el.innerHTML = icon(type === "error" ? "alert-circle" : "check-circle-2") + "<span></span>";
      el.querySelector("span").textContent = msg;
      document.body.appendChild(el);
      renderIcons(el);
      setTimeout(function () { el.remove(); }, 4000);
    };

    /* copy-to-clipboard buttons [data-copy] */
    document.querySelectorAll("[data-copy]").forEach(function (btn) {
      btn.addEventListener("click", function () {
        var text = btn.dataset.copy || "";
        function done() { window.showToast(btn.dataset.copyMsg || "কপি হয়েছে ✓"); }
        if (navigator.clipboard && navigator.clipboard.writeText) {
          navigator.clipboard.writeText(text).then(done, done);
        } else { done(); }
      });
    });

    /* char counters: textarea[data-counter="#id"] */
    document.querySelectorAll("textarea[data-counter]").forEach(function (ta) {
      var counter = document.querySelector(ta.dataset.counter);
      if (!counter) return;
      var max = parseInt(ta.getAttribute("maxlength") || "5000", 10);
      function update() {
        var n = ta.value.length;
        counter.textContent = n.toLocaleString("bn-BD") + " / " + max.toLocaleString("bn-BD");
        counter.classList.toggle("warn", n > max * 0.9);
      }
      ta.addEventListener("input", update);
      update();
    });

    /* demo confirm dialogs [data-confirm] */
    document.querySelectorAll("[data-confirm]").forEach(function (el) {
      el.addEventListener("click", function (e) {
        if (!window.confirm(el.dataset.confirm)) e.preventDefault();
      });
    });

    /* composer autogrow */
    document.querySelectorAll(".composer textarea, textarea.autogrow").forEach(function (ta) {
      ta.addEventListener("input", function () {
        ta.style.height = "auto";
        ta.style.height = Math.min(ta.scrollHeight, 130) + "px";
      });
    });

    /* chat mode switch (Ask vs Search) — visual only */
    var composerMode = document.getElementById("composer-mode");
    if (composerMode) {
      composerMode.addEventListener("chipchange", function (e) {
        var ta = document.querySelector(".composer textarea");
        if (!ta) return;
        ta.placeholder = e.detail.textContent.indexOf("খুঁজ") !== -1
          ? "আইনের কীওয়ার্ড লিখুন... (যেমন: বেতন, ধারা ১২৩)"
          : "আপনার সমস্যা লিখুন... (বাংলা / English / Banglish)";
      });
    }

    renderIcons();
  });
})();
