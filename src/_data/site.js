// Site-wide settings. Change the domain here (one place) if it ever changes.
const repo = "brsi4653-wq/globalmayhem";

export default {
  name: "Global Mayhem", // official and legal name: titles, footer, metadata
  displayName: "MAYHEM", // the name shown in the header and logo lockups
  description:
    "Global Mayhem makes OnRecord, a glass Alt+Space popup for Windows that runs your own Claude Code.",
  url: (process.env.SITE_URL || "https://globalmayhem.org").replace(/\/$/, ""),
  language: "en-CA",
  // The X account named in link previews.
  xHandle: process.env.X_HANDLE || "@GlobalMayhemCA",
  // OnRecord, our product. The code is in the "onrecord" folder of this repo.
  // The download link always points at the newest release, so it never needs changing.
  onrecord: {
    name: "OnRecord",
    line: "A glass Alt+Space popup for Windows that runs your own Claude Code.",
    logo: "/assets/onrecord/logo.png",
    download: `https://github.com/${repo}/releases/latest/download/OnRecord-Setup.exe`,
    latestApi: `https://api.github.com/repos/${repo}/releases/latest`,
    releases: `https://github.com/${repo}/releases/latest`,
    code: `https://github.com/${repo}/tree/main/onrecord`,
    license: `https://github.com/${repo}/blob/main/onrecord/LICENSE`,
  },
  // Motion and visual effects. Each name here switches one effect on.
  // Choices: scramble, wipe, reveal, hover, progress, transitions, grain,
  // intro (opening sequence), hero (big titles rise in), marquee (moving band), cursor (pink pointer)
  effects: ["scramble", "wipe", "reveal", "hover", "progress", "transitions", "grain", "intro", "hero", "marquee"],
  allEffects: ["scramble", "wipe", "reveal", "hover", "progress", "transitions", "grain", "intro", "hero", "marquee", "cursor"],
  // Preview builds show every effect with an on/off panel. Never used on the real site.
  preview: process.env.PREVIEW_FX === "1",
};
