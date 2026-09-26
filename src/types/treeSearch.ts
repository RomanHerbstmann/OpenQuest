import type { SearchResult } from '@openquest/tree-search';

export type { Interpretation, ResultItem } from '@openquest/tree-search';

/** Response of `POST /api/tree-search`: the search result without the raw index list, plus map positions. */
export type TreeSearchResponse = Omit<SearchResult, 'matches'> & {
  /** `[lat, lon]` of the matching trees, evenly sampled when there are more than `MAX_POSITIONS`. */
  positions: Array<[number, number]>;
  /** True when `positions` is a sample and not every match. */
  positionsSampled: boolean;
};

export type TreeSearchError = { error: string };
