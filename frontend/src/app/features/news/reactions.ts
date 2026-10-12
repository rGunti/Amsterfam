import { NEWS_REACTIONS, NewsReactionCount } from '../../core/models/news';

/** Anything that carries reaction counts and the viewer's own reactions (posts, comments). */
export interface Reactable {
  reactions: NewsReactionCount[];
  myReactions: string[];
}

const reactionOrder = (emoji: string) => (NEWS_REACTIONS as readonly string[]).indexOf(emoji);

/**
 * The viewer's reaction added or removed, as shown straight away while the request runs.
 * Counts stay in {@link NEWS_REACTIONS} order and drop out at zero.
 */
export function toggleReaction<T extends Reactable>(item: T, emoji: string): T {
  const mine = item.myReactions.includes(emoji);
  const count = (item.reactions.find((r) => r.emoji === emoji)?.count ?? 0) + (mine ? -1 : 1);
  const others = item.reactions.filter((r) => r.emoji !== emoji);
  const reactions = count > 0 ? [...others, { emoji, count }] : others;
  return {
    ...item,
    reactions: reactions.sort((a, b) => reactionOrder(a.emoji) - reactionOrder(b.emoji)),
    myReactions: mine ? item.myReactions.filter((e) => e !== emoji) : [...item.myReactions, emoji],
  };
}
