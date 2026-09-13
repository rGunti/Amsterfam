export interface User {
  id: number;
  handle: string;
  displayName: string | null;
  email: string;
  avatarUrl: string | null;
}

export interface UpdateUserRequest {
  displayName: string | null;
  avatarUrl: string | null;
}
