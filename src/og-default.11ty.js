// The fallback link preview image, used by every page that is not an article.
import { ogCard } from "../lib/og-card.js";

export default class {
  data() {
    return { permalink: "/og/onrecord-card.png", eleventyExcludeFromCollections: true };
  }

  async render(data) {
    return ogCard({
      headline: data.site.onrecord.name,
      label: "Our product",
      footer: data.site.url.replace(/^https?:\/\//, ""),
    });
  }
}
