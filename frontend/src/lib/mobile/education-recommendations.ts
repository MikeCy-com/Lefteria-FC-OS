import {
  ACADEMY_EDUCATION_ARTICLES,
  type AcademyEducationArticle,
  type EducationAudience,
  type EducationStage,
  type EducationTopic
} from './academy-education.ts';

/**
 * Privacy-preserving recommendation ordering for the public starter library.
 * This is NOT an entitlement check. Published/private HUB articles must be
 * authorized by the server before they can ever be passed to a client.
 */
export interface EducationRecommendationRequest {
  readonly role: 'parent' | 'player' | 'fan';
  readonly stage: EducationStage;
  readonly preferredTopics?: readonly EducationTopic[];
  readonly completedArticleIds?: readonly string[];
  readonly limit?: number;
}

export function recommendStarterEducation(
  request: EducationRecommendationRequest
): readonly AcademyEducationArticle[] {
  // Fans never receive article bodies from the recommendation helper.
  // A fan's interest selection cannot elevate them to a guardian/player.
  if (request.role === 'fan') return [];
  const audience: EducationAudience = request.role;
  const stages: readonly EducationStage[] = ['6-9', '10-12', '13-15', '16-18'];
  if (!stages.includes(request.stage)) return [];
  const allowedTopics: readonly EducationTopic[] =
    ['support', 'psychology', 'communication', 'wellbeing'];
  const preferences = new Set(
    (request.preferredTopics ?? []).filter(topic => allowedTopics.includes(topic))
  );
  const completed = new Set(
    (request.completedArticleIds ?? []).filter(id => typeof id === 'string' && id.length <= 120)
  );
  const limit = Number.isSafeInteger(request.limit) ?
    Math.max(0, Math.min(10, request.limit!)) : 4;

  return ACADEMY_EDUCATION_ARTICLES
    .filter(article => article.audience === audience && article.stage === request.stage)
    .map((article, index) => ({
      article,
      index,
      score: (preferences.has(article.topic) ? 2 : 0) +
        (completed.has(article.id) ? -4 : 0)
    }))
    .sort((a, b) => b.score - a.score || a.index - b.index)
    .slice(0, limit)
    .map(entry => entry.article);
}
