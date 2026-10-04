export interface Birthday {
  month: number;
  day: number;
  year: number | null;
}

export interface DietaryOption {
  id: number;
  key: string;
  label: string;
}

/** The "about me" fields every member can fill in and fellow members can see. */
export interface ProfileDetails {
  pronouns: string | null;
  location: string | null;
  bio: string | null;
  birthday: Birthday | null;
  dietaryOptions: DietaryOption[];
  dietaryNotes: string | null;
}

export interface User extends ProfileDetails {
  id: number;
  handle: string;
  /** Sign-in source slug, e.g. "discord"; null until known. Handles are unique per source. */
  authSource: string | null;
  /** "handle@source", the key in profile links (/users/:handle). */
  profileHandle: string;
  displayName: string | null;
  email: string;
  avatarUrl: string | null;
}

/** Someone else's profile, as fellow members see it (no email). */
export interface UserProfile extends ProfileDetails {
  id: number;
  handle: string;
  authSource: string | null;
  profileHandle: string;
  displayName: string | null;
  avatarUrl: string | null;
}

export interface UpdateUserRequest {
  displayName: string | null;
  avatarUrl: string | null;
}

/** PUT /me/about replaces all of the "about me" fields at once. */
export interface UpdateAboutRequest {
  pronouns: string | null;
  location: string | null;
  bio: string | null;
  birthday: Birthday | null;
  dietaryOptionIds: number[];
  dietaryNotes: string | null;
}

export const MAX_DISPLAY_NAME_LENGTH = 100;
export const MAX_PRONOUNS_LENGTH = 40;
export const MAX_LOCATION_LENGTH = 100;
export const MAX_DIETARY_NOTES_LENGTH = 500;
export const MAX_BIO_LENGTH = 1000;
