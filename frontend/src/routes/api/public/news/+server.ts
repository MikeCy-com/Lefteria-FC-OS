import { isHttpError, json } from '@sveltejs/kit';
import type { RequestHandler } from './$types';
import { requestPublicSearchNews } from '../../../../lib/server/public-content';

export const GET: RequestHandler = async ({ url }) => {
  try {
    const result = await requestPublicSearchNews(url);
    return result.status === 200 && result.value ? json(result.value, { headers: { 'cache-control': 'no-store' } }) :
      json({ detail: 'Τα νέα δεν είναι προσωρινά διαθέσιμα.', code: 'public_content_unavailable' }, { status: 503, headers: { 'cache-control': 'no-store' } });
  } catch (error) {
    if (isHttpError(error, 400)) return json({ detail: 'Μη έγκυρα φίλτρα.', code: 'invalid_filters' }, { status: 400, headers: { 'cache-control': 'no-store' } });
    throw error;
  }
};
