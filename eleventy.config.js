import site from "./src/_data/site.js";

// Only the "src" folder is built into the site. The "onrecord" folder
// (the OnRecord app's code) sits outside it and is never published.
export default function (eleventyConfig) {
  eleventyConfig.addPassthroughCopy({ "src/assets": "assets" });

  const buildTime = new Date();
  eleventyConfig.addGlobalData("buildTime", buildTime);

  // A number that changes on every build. Added to the style file's address
  // so browsers fetch the new styles instead of using an old saved copy.
  eleventyConfig.addFilter("version", (d) => String(new Date(d).getTime()));
  eleventyConfig.addFilter("year", (d) => new Date(d).getFullYear());
  eleventyConfig.addFilter("absolute", (path) => site.url + path);
  // Safe JSON for <script type="application/ld+json">
  eleventyConfig.addFilter("jsonld", (obj) =>
    JSON.stringify(obj).replace(/</g, "\\u003c")
  );

  return {
    dir: { input: "src", output: "_site", includes: "_includes", data: "_data" },
    markdownTemplateEngine: false,
    htmlTemplateEngine: "njk",
  };
}
