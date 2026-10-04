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
  displayName: string | null;
  email: string;
  avatarUrl: string | null;
}

/** Someone else's profile, as fellow members see it (no email). */
export interface UserProfile extends ProfileDetails {
  id: number;
  handle: string;
  displayName: string | null;
  avatarUrl: string | null;
}

/** PUT /me replaces the whole profile, so every field is sent each time. */
export interface UpdateUserRequest {
  displayName: string | null;
  avatarUrl: string | null;
  pronouns: string | null;
  location: string | null;
  bio: string | null;
  birthday: Birthday | null;
  dietaryOptionIds: number[];
  dietaryNotes: string | null;
}

/** The current profile as an update request, with `patch` applied on top. */
export function toUpdateRequest(user: User, patch: Partial<UpdateUserRequest>): UpdateUserRequest {
  return {
    displayName: user.displayName,
    avatarUrl: user.avatarUrl,
    pronouns: user.pronouns,
    location: user.location,
    bio: user.bio,
    birthday: user.birthday,
    dietaryOptionIds: user.dietaryOptions.map((o) => o.id),
    dietaryNotes: user.dietaryNotes,
    ...patch,
  };
}

export const MAX_PRONOUNS_LENGTH = 40;
export const MAX_LOCATION_LENGTH = 100;
export const MAX_DIETARY_NOTES_LENGTH = 500;
export const MAX_BIO_LENGTH = 1000;
