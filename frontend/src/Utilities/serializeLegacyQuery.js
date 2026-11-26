import $ from 'jquery';

/**
 * Serialize the existing legacy traditional query format at the JS boundary.
 * @param {Record<string, unknown>} params
 * @returns {string}
 */
function serializeLegacyQuery(params) {
  return $.param(params, true);
}

export default serializeLegacyQuery;
