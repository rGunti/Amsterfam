export interface PaymentMethod {
  id: number;
  title: string;
  icon: string | null;
  link: string | null;
  description: string | null;
}

export interface UpsertPaymentMethodRequest {
  title: string;
  icon: string | null;
  link: string | null;
  description: string | null;
}
