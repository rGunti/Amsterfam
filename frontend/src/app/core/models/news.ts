import { TimelineUser } from './timeline';

export interface NewsPost {
  id: number;
  title: string | null;
  /** Raw Markdown. */
  body: string;
  author: TimelineUser;
  createdAt: string;
  publishedAt: string | null;
  editedAt: string | null;
  canEdit: boolean;
}

export interface NewsFeed {
  /** Newest first. */
  posts: NewsPost[];
  /** When the viewer last opened the feed; later posts are new to them. */
  seenAt: string | null;
}

export interface UpsertNewsPostRequest {
  title: string | null;
  body: string;
}

export const NEWS_TITLE_MAX = 120;
export const NEWS_BODY_MAX = 4000;
