# Public page structured data

The shared head publishes one JSON-LD graph. Organization and WebSite identify the publisher;
each document has one page entity. The homepage also describes the site application and competition service.

- Athlete URLs use ProfilePage with a main Person at the canonical athlete URL plus `#person`.
  Ranking entries refer to that same identity. Only the public display name, profile URL and
  versioned portrait are included; private contact details and birth dates are not copied into the graph.
- Full ranking pages use CollectionPage and an ordered ItemList. The server uses AthleteDataService's
  competition order and the request's clock and cohort filters, including qualification and tie-breaks.
  After hydration, the leaderboard replaces this list with its actual displayed entries. ListItem positions
  are the displayed ranks, including gaps after searching. Empty selections have zero items.
- Search includes rendered badge text and score precision. Search URLs therefore defer their ItemList
  until the normal client rendering has resolved the exact matches. They never publish an unfiltered list.
- About uses AboutPage. The existing History document has an Article with its visible headline.
  No author, publication date, or video metadata is inferred from file timestamps or external links.
  The media kit remains a CollectionPage, not a video watch page.

Canonical document URLs continue to follow the routing policy. Lists for combined selections have
their own selection URLs/identifiers, while Person identifiers do not change with clock or share context.
Server JSON uses System.Text.Json's default HTML-safe escaping. Client updates use textContent and
escape `<`; never concatenate public text into a script element as HTML.

Keep schema changes invisible in the interface. Regression tests compare initial HTML, hydrated JSON-LD,
and the visible ranking rows. Profile hydration must not replace its Person with the background leaderboard.

References: [Schema.org ProfilePage](https://schema.org/ProfilePage),
[ItemList](https://schema.org/ItemList), and
[Google's structured-data content policies](https://developers.google.com/search/docs/appearance/structured-data/sd-policies).
