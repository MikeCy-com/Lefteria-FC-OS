/** Only the server may decide whether an article is published and role-eligible. */
export interface PublishedHubArticle {
  id:string;
  organization_id:'lefteria_fc'|'lefteria_fc_academy';
  title:string;
  body:string;
  published_at:string;
  audience:'parent'|'player';
  age_min:number|null;
  age_max:number|null;
}
export interface PublishedHubPage {
  organization_id:'lefteria_fc'|'lefteria_fc_academy';
  audience:'parent'|'player';
  items:PublishedHubArticle[];
}
const owner=(value:unknown):value is PublishedHubArticle['organization_id']=>
  value==='lefteria_fc'||value==='lefteria_fc_academy';
const age=(value:unknown)=>value===null||(typeof value==='number'&&
  Number.isSafeInteger(value)&&value>=4&&value<=99);
export function validPublishedHubPage(value:unknown,expected:'parent'|'player',
  organization:PublishedHubArticle['organization_id']):value is PublishedHubPage {
  if(!value||typeof value!=='object'||Array.isArray(value))return false;
  const page=value as Record<string,unknown>;
  if(!owner(page.organization_id)||page.organization_id!==organization||
     page.audience!==expected||!Array.isArray(page.items)||page.items.length>20)return false;
  return page.items.every((item:unknown)=>{
    if(!item||typeof item!=='object'||Array.isArray(item))return false;
    const a=item as Record<string,unknown>;
    return typeof a.id==='string'&&/^[a-f0-9-]{36}$/i.test(a.id)&&
      a.organization_id===organization&&a.audience===expected&&
      typeof a.title==='string'&&a.title.length>=1&&a.title.length<=180&&
      typeof a.body==='string'&&a.body.length>=1&&a.body.length<=20000&&
      typeof a.published_at==='string'&&!Number.isNaN(Date.parse(a.published_at))&&
      age(a.age_min)&&age(a.age_max)&&
      ((a.age_min===null&&a.age_max===null)||
       (typeof a.age_min==='number'&&typeof a.age_max==='number'&&a.age_max>=a.age_min))&&
      (expected!=='player'||(a.age_min===null&&a.age_max===null));
  });
}


/**
 * Searches only a page of articles ALREADY authorized and returned by the
 * server. Search never changes the audience, organization or entitlement
 * boundary and must never be used to authorize a result.
 */
export function searchPublishedHubEducation(
  items: readonly PublishedHubArticle[], query: string
): readonly PublishedHubArticle[] {
  const normalized = query.trim().slice(0, 120)
    .normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('el-GR');
  const tokens = normalized.split(/\s+/).filter(Boolean);
  if (!tokens.length) return items;
  return items.filter(article => {
    const searchable = (article.title + ' ' + article.body)
      .normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('el-GR');
    return tokens.every(token => searchable.includes(token));
  });
}


/**
 * Session-local progression of articles already authorized by the backend.
 * This is only a stable ordering of the supplied list: it never grants an
 * audience, changes organization, fetches articles or derives child age.
 */
export function prioritizeUncompletedPublishedEducation(
  authorized: readonly PublishedHubArticle[], completedIds: readonly string[]
): readonly PublishedHubArticle[] {
  const completed=new Set(completedIds.filter(id=>typeof id==='string'&&id.length<=120));
  return [...authorized].sort((a,b)=>
    Number(completed.has(a.id))-Number(completed.has(b.id)));
}


/** An individual's next eligible approved lesson, not a global latest feed. */
export interface PublishedHubNextArticle extends PublishedHubArticle {
  revision_id:string;
}
export interface PublishedHubNextPage {
  organization_id:PublishedHubArticle['organization_id'];
  audience:'parent'|'player';
  next:PublishedHubNextArticle|null;
}
export interface PublishedHubCompletion {
  organization_id:PublishedHubArticle['organization_id'];
  audience:'parent'|'player';
  completed_id:string;
  completed_revision_id:string;
}
const uuid=(v:unknown)=>typeof v==='string'&&
  /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(v);
export function validPublishedHubNext(value:unknown,role:'parent'|'player',
  organization:PublishedHubArticle['organization_id']):value is PublishedHubNextPage {
  if(!value||typeof value!=='object'||Array.isArray(value))return false;
  const page=value as Record<string,unknown>;
  if(page.organization_id!==organization||page.audience!==role)return false;
  if(page.next===null)return true;
  const next=page.next;
  if(!next||typeof next!=='object'||Array.isArray(next))return false;
  const a=next as Record<string,unknown>;
  return uuid(a.id)&&uuid(a.revision_id)&&a.organization_id===organization&&
    a.audience===role&&typeof a.title==='string'&&a.title.trim().length>0&&
    a.title.length<=180&&typeof a.body==='string'&&a.body.trim().length>0&&
    a.body.length<=20000&&typeof a.published_at==='string'&&
    !Number.isNaN(Date.parse(a.published_at))&&
    Date.parse(a.published_at)<=Date.now()&&a.age_min===null&&a.age_max===null;
}
export function validPublishedHubCompletion(value:unknown,
  page:PublishedHubNextPage):value is PublishedHubCompletion {
  if(!value||typeof value!=='object'||Array.isArray(value)||!page.next)return false;
  const result=value as Record<string,unknown>;
  return result.organization_id===page.organization_id&&
    result.audience===page.audience&&
    result.completed_id===page.next.id&&
    result.completed_revision_id===page.next.revision_id;
}
