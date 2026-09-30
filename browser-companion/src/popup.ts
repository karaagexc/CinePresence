const el = (id: string): HTMLElement => document.getElementById(id)!;
const input = (id: string): HTMLInputElement => el(id) as HTMLInputElement;
async function update(type = "status") {
  try {
    const reply: CinePresence.PopupState = await chrome.runtime.sendMessage({ type, title: input("title").value, season: input("season").value, episode: input("episode").value });
    el("status").textContent = reply.status;
    el("source").textContent = reply.excluded ? "This page is excluded from sharing." : reply.detected ? "Video detected. Page metadata is sent only to your local app." : "Start a video, or reload this tab after enabling the companion.";
    for (const control of document.querySelectorAll<HTMLInputElement | HTMLButtonElement>("input,button")) control.disabled = reply.excluded || !reply.detected;
    if (type === "manual") el("source").textContent = "Title sent. Check CinePresence for the TMDB match.";
  } catch { el("status").textContent = "Reopen the extension to reconnect."; }
}
el("manual").addEventListener("submit", e => {
  e.preventDefault();
  if (Boolean(input("season").value) !== Boolean(input("episode").value)) { el("source").textContent = "Enter both season and episode, or leave both empty."; return; }
  update("manual");
});
el("reset").addEventListener("click", () => update("reset"));
update(); setInterval(() => update(), 2000);
