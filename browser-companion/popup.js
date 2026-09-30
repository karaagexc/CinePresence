const el = id => document.getElementById(id);
async function update(type = "status") {
  try {
    const reply = await chrome.runtime.sendMessage({ type, title: el("title").value, season: el("season").value, episode: el("episode").value });
    el("status").textContent = reply.status;
    el("source").textContent = reply.excluded ? "This page is excluded from sharing." : reply.detected ? "Video detected. Page metadata is sent only to your local app." : "Start a video, or reload this tab after enabling the companion.";
    for (const control of document.querySelectorAll("input,button")) control.disabled = reply.excluded || !reply.detected;
    if (type === "manual") el("source").textContent = "Title sent. Check CinePresence for the TMDB match.";
  } catch { el("status").textContent = "Reopen the extension to reconnect."; }
}
el("manual").addEventListener("submit", e => {
  e.preventDefault();
  if (Boolean(el("season").value) !== Boolean(el("episode").value)) { el("source").textContent = "Enter both season and episode, or leave both empty."; return; }
  update("manual");
});
el("reset").addEventListener("click", () => update("reset"));
update(); setInterval(() => update(), 2000);
