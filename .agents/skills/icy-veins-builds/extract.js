// Reads every Icy Veins Heroes of the Storm build guide into the extract that
// `heroesreplay builds import` checks (#412). Run it in a real browser tab already open on any
// https://www.icy-veins.com/heroes/ page (the built-in browser or Claude in Chrome), never from a
// plain HTTP client. It fetches one guide every 2 seconds from the page's own origin, keeps only
// facts (build names, tags, per-tier marks, recommended talent slugs, calculator codes, the
// guide's "last updated" day) and no guide text.
//
// It starts in the background and returns at once. Poll `window.__icyVeinsExtract.done`, then read
// `window.__icyVeinsExtract.json` (in slices if the tool output is capped) into a file.
(() => {
  const state = { done: false, guides: [], errors: [], json: null };
  window.__icyVeinsExtract = state;

  const slugs = [
    ...new Set(
      [...document.querySelectorAll('a[href]')]
        .map((a) => a.getAttribute('href'))
        .map((href) => /\/heroes\/([a-z0-9-]+)-build-guide$/.exec(href || ''))
        .filter((match) => match !== null)
        .map((match) => match[1])
    ),
  ];

  const tierMark = (span) => {
    const match = /heroes_build_talent_tier_(yes|no|situational)/.exec(span.className);
    return match ? match[1][0] : '?';
  };

  const extract = (html, slug) => {
    const page = new DOMParser().parseFromString(html, 'text/html');
    const updated =
      page.querySelector('.page_author .local_date_date')?.textContent.trim() ?? null;
    const calculators = [
      ...page.querySelectorAll(`a[href*="talent-calculator/${slug}#"]`),
    ].map((a) => a.getAttribute('href').split('#')[1]);
    const builds = [...page.querySelectorAll('div.heroes_builds div.heroes_build')].map(
      (build, index) => ({
        name: build.querySelector('.heroes_build_header h3')?.textContent.trim() ?? null,
        tag:
          [...build.querySelectorAll('.heroes_build_tag')]
            .map((tag) => tag.textContent.trim())
            .join('+') || null,
        calculator: calculators[index] ?? null,
        tiers: [...build.querySelectorAll('.heroes_build_talent_tier')].map((tier) => ({
          label:
            tier
              .querySelector('.heroes_build_talent_tier_subtitle')
              ?.textContent.replace('Level', '')
              .trim() ?? null,
          marks: [...tier.querySelectorAll('.heroes_build_talent_tier_visual span')]
            .map(tierMark)
            .join(''),
          recommended: [
            ...tier.querySelectorAll('a.heroes_build_talent_tier_recommended[data-heroes-tooltip]'),
          ].map((a) => a.getAttribute('data-heroes-tooltip').replace(`${slug}-talent-`, '')),
        })),
      })
    );
    return { slug, updated, builds };
  };

  (async () => {
    for (const slug of slugs) {
      try {
        const response = await fetch(`/heroes/${slug}-build-guide`, { credentials: 'same-origin' });
        if (response.ok) {
          state.guides.push(extract(await response.text(), slug));
        } else {
          state.errors.push(`${slug}: HTTP ${response.status}`);
        }
      } catch (error) {
        state.errors.push(`${slug}: ${error.message}`);
      }

      await new Promise((resolve) => setTimeout(resolve, 2000));
    }

    state.json = JSON.stringify(state.guides);
    state.done = true;
  })();

  return { guides: slugs.length, first: slugs.slice(0, 3) };
})();
