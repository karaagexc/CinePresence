(() => {
  const folder = document.getElementById("folder")!;
  const copy = document.getElementById("copy")!;
  const path = decodeURIComponent(new URL("browser-companion/", location.href).pathname)
    .replace(/^\/(?=[A-Za-z]:)/, "").replaceAll("/", "\\").replace(/\\$/, "");
  folder.textContent = path;
  copy.addEventListener("click", async () => {
    try { await navigator.clipboard.writeText(path); copy.textContent = "Copied"; }
    catch {
      const selection = getSelection();
      if (!selection) return;
      selection.removeAllRanges();
      const range = document.createRange(); range.selectNodeContents(folder); selection.addRange(range);
      copy.textContent = "Press Ctrl+C to copy";
    }
  });
})();
