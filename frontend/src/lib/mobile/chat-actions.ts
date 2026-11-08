import type { MobileRole } from './navigation.ts';

export type ChatAction = 'edit' | 'delete' | 'react' | 'seen-by' | 'delete-chat-for-me';
export type ChatSurface =
  | { kind:'message'; role:MobileRole; actorId:string; senderId:string;
      isDeleted:boolean; isParticipant:boolean; canReply:boolean;
      canEdit:boolean; canDelete:boolean; canReact:boolean; canSeeReaders:boolean }
  | { kind:'announcement'; role:MobileRole; isVisible:boolean; isDeleted:boolean;
      canReact:boolean }
  | { kind:'conversation'; role:MobileRole; conversationKind:'private'|'group';
      isParticipant:boolean; canHideForSelf:boolean };

export const CHAT_REACTION_EMOJIS = ['👍','❤️','😂','⚽','👏','🎉','🙏','🔥'] as const;
export type ChatReaction = typeof CHAT_REACTION_EMOJIS[number];

/**
 * UI exposure only; API and database must re-authorize the *current* session
 * and canonical membership for every read/write, including named read receipts.
 * Never infer admin role from a display name or a cached sender avatar.
 */
export function availableChatActions(context:ChatSurface): readonly ChatAction[] {
  if(context.kind==='announcement')
    return context.isVisible && !context.isDeleted && context.canReact?['react']:[];
  if(context.kind==='conversation')
    return context.conversationKind==='private' &&
      context.isParticipant && context.canHideForSelf ? ['delete-chat-for-me'] : [];
  if(!context.isParticipant || context.isDeleted)return [];
  const own=Boolean(context.actorId) && context.actorId===context.senderId;
  const actions:ChatAction[]=[];
  if(own && context.canReply && context.canEdit)actions.push('edit');
  if(own && context.canReply && context.canDelete)actions.push('delete');
  if(context.canReply && context.canReact)actions.push('react');
  if(own && context.role==='management' && context.canSeeReaders)
    actions.push('seen-by');
  return actions;
}

export function validReaders(
  input:unknown,maximum=150
):input is {readers:{id:string;name:string}[]} {
  if(!input || typeof input!=='object' || Array.isArray(input))return false;
  const row=input as Record<string,unknown>;
  if(!Array.isArray(row.readers) || row.readers.length>maximum)return false;
  const keys=new Set<string>();
  return row.readers.every((value:unknown)=>{
    if(!value || typeof value!=='object' || Array.isArray(value))return false;
    const person=value as Record<string,unknown>;
    // An unexpected raw contact or medical field is not part of this safe DTO.
    if(Object.keys(person).sort().join(',')!=='id,name')return false;
    if(typeof person.id!=='string' || person.id.length===0 || person.id.length>128 ||
      typeof person.name!=='string' || person.name.trim().length===0 ||
      person.name.length>180 || keys.has(person.id))return false;
    keys.add(person.id);
    return true;
  });
}
