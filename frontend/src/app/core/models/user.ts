export interface User {
  id: number;
  displayName: string;
  email: string;
  avatarUrl: string | null;
}

export interface UpdateUserRequest {
  displayName: string;
  avatarUrl: string | null;
}
