import type { MobileRole } from '../navigation.ts';

export type RememberDays = null | 30 | 60 | 90;
export type MobileLoginStep = 'phone' | 'otp' | 'choose_role' | 'fan_register';
export type UiAction = void | Promise<void>;

export interface MobileIdentityOption {
  role: MobileRole;
  name: string;
}

export interface FanRegistrationForm {
  firstName: string;
  lastName: string;
  phone: string;
  birthday: string;
}

export interface OrganizationOption {
  id: string;
  name: string;
}

export type MobileIconName = 'home' | 'calendar' | 'users' | 'messages' | 'menu' |
  'bell' | 'user' | 'plus' | 'chevron' | 'arrow' | 'phone' | 'shield' | 'trophy' |
  'cart' | 'wallet' | 'grid' | 'back' | 'check';
