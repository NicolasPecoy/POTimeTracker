(() => {
  "use strict";

  document.getElementById("year").textContent = new Date().getFullYear();

  // Nav bar: add border/blur once the page scrolls.
  const nav = document.getElementById("nav");
  const onScroll = () => nav.classList.toggle("scrolled", window.scrollY > 8);
  onScroll();
  window.addEventListener("scroll", onScroll, { passive: true });

  // Scroll-reveal for sections, using IntersectionObserver.
  const revealEls = document.querySelectorAll(".reveal");
  if ("IntersectionObserver" in window) {
    const io = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (entry.isIntersecting) {
            entry.target.classList.add("in");
            io.unobserve(entry.target);
          }
        }
      },
      { threshold: 0.15, rootMargin: "0px 0px -40px 0px" }
    );
    revealEls.forEach((el) => io.observe(el));
  } else {
    revealEls.forEach((el) => el.classList.add("in"));
  }

  // Best-effort: show the latest release version pulled live from GitHub.
  // Falls back to static copy if the API call fails or is rate-limited.
  fetch("https://api.github.com/repos/NicolasPecoy/POTimeTracker/releases/latest")
    .then((res) => (res.ok ? res.json() : Promise.reject(res.status)))
    .then((data) => {
      const tag = (data && data.tag_name) || "";
      if (!tag) return;
      const versionTag = document.getElementById("version-tag");
      const latestVersion = document.getElementById("latest-version");
      if (versionTag) {
        versionTag.innerHTML = `Última versión: <strong>${tag}</strong>`;
      }
      if (latestVersion) {
        latestVersion.textContent = `última versión: ${tag}`;
      }
    })
    .catch(() => {
      const versionTag = document.getElementById("version-tag");
      if (versionTag) versionTag.textContent = "Ver todas las versiones en GitHub Releases";
    });
})();
