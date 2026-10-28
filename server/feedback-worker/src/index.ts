// Placeholder entry so the Worker config resolves; the real handler arrives in the next task.
export default {
  fetch(): Response {
    return new Response('not implemented', { status: 501 });
  },
};
