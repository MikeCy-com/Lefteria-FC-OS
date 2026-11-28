# NewsArticle structured data

Published, available news details on the recognized public origin now emit one Schema.org NewsArticle JSON-LD document with headline, canonical URL, optional visible excerpt, safe published HTTPS image and the source creation timestamp. Existing canonical URLs remain unchanged. No author, publisher, publication or modification timestamp is invented; these need verified editorial fields before adding them.

A dedicated serializer allowlists fields and escapes HTML raw-text delimiters. Tests exercise actual Svelte SSR head output with script-breaking input, preview/unavailable/wrong-owner suppression and field omission. This is a news-only structured-data slice, not sitewide SEO completion or a guarantee of search rich results. Hosted crawl and rich-result validation are still required; no production deployment authorized.
