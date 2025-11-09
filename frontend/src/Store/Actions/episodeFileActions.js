import _ from 'lodash';
import { createAction } from 'redux-actions';
import { batchActions } from 'redux-batched-actions';
import episodeEntities from 'Episode/episodeEntities';
import { createThunk, handleThunks } from 'Store/thunks';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import { removeItem, set, updateItem } from './baseActions';
import createFetchHandler from './Creators/createFetchHandler';
import createHandleActions from './Creators/createHandleActions';
import createRemoveItemHandler from './Creators/createRemoveItemHandler';

//
// Variables

export const section = 'episodeFiles';

//
// State

export const defaultState = {
  isFetching: false,
  isPopulated: false,
  error: null,
  isDeleting: false,
  deleteError: null,
  isSaving: false,
  saveError: null,
  items: []
};

//
// Action Types

export const FETCH_EPISODE_FILE = 'episodeFiles/fetchEpisodeFile';
export const FETCH_EPISODE_FILES = 'episodeFiles/fetchEpisodeFiles';
export const DELETE_EPISODE_FILE = 'episodeFiles/deleteEpisodeFile';
export const DELETE_EPISODE_FILES = 'episodeFiles/deleteEpisodeFiles';
export const UPDATE_EPISODE_FILES = 'episodeFiles/updateEpisodeFiles';
export const MOVE_EPISODE_FILE = 'episodeFiles/moveEpisodeFile';
export const ASSOCIATE_EPISODE_FILE = 'episodeFiles/associateEpisodeFile';
export const UNASSOCIATE_EPISODE_FILE = 'episodeFiles/unassociateEpisodeFile';
export const CLEAR_EPISODE_FILES = 'episodeFiles/clearEpisodeFiles';

//
// Action Creators

export const fetchEpisodeFile = createThunk(FETCH_EPISODE_FILE);
export const fetchEpisodeFiles = createThunk(FETCH_EPISODE_FILES);
export const deleteEpisodeFile = createThunk(DELETE_EPISODE_FILE);
export const deleteEpisodeFiles = createThunk(DELETE_EPISODE_FILES);
export const updateEpisodeFiles = createThunk(UPDATE_EPISODE_FILES);
export const moveEpisodeFile = createThunk(MOVE_EPISODE_FILE);
export const associateEpisodeFile = createThunk(ASSOCIATE_EPISODE_FILE);
export const unassociateEpisodeFile = createThunk(UNASSOCIATE_EPISODE_FILE);
export const clearEpisodeFiles = createAction(CLEAR_EPISODE_FILES);

//
// Helpers

const deleteEpisodeFileHelper = createRemoveItemHandler(section, '/episodeFile');

//
// Action Handlers

export const actionHandlers = handleThunks({
  [FETCH_EPISODE_FILE]: createFetchHandler(section, '/episodeFile'),
  [FETCH_EPISODE_FILES]: createFetchHandler(section, '/episodeFile'),

  [DELETE_EPISODE_FILE]: function(getState, payload, dispatch) {
    const {
      id: episodeFileId,
      episodeEntity = episodeEntities.EPISODES
    } = payload;

    const episodeSection = _.last(episodeEntity.split('.'));
    const deletePromise = deleteEpisodeFileHelper(getState, payload, dispatch);

    deletePromise.done(() => {
      const episodes = getState().episodes.items;
      const episodesWithRemovedFiles = _.filter(episodes, { episodeFileId });

      dispatch(batchActions([
        ...episodesWithRemovedFiles.map((episode) => {
          return updateItem({
            section: episodeSection,
            ...episode,
            episodeFileId: 0,
            hasFile: false
          });
        })
      ]));
    });
  },

  [DELETE_EPISODE_FILES]: function(getState, payload, dispatch) {
    const {
      episodeFileIds
    } = payload;

    dispatch(set({ section, isDeleting: true }));

    const promise = createAjaxRequest({
      url: '/episodeFile/bulk',
      method: 'DELETE',
      dataType: 'json',
      data: JSON.stringify({ episodeFileIds })
    }).request;

    promise.done(() => {
      const episodes = getState().episodes.items;
      const episodesWithRemovedFiles = episodeFileIds.reduce((acc, episodeFileId) => {
        acc.push(..._.filter(episodes, { episodeFileId }));

        return acc;
      }, []);

      dispatch(batchActions([
        ...episodeFileIds.map((id) => {
          return removeItem({ section, id });
        }),

        ...episodesWithRemovedFiles.map((episode) => {
          return updateItem({
            section: 'episodes',
            ...episode,
            episodeFileId: 0,
            hasFile: false
          });
        }),

        set({
          section,
          isDeleting: false,
          deleteError: null
        })
      ]));
    });

    promise.fail((xhr) => {
      dispatch(set({
        section,
        isDeleting: false,
        deleteError: xhr
      }));
    });
  },

  [UPDATE_EPISODE_FILES]: function(getState, payload, dispatch) {
    const { files } = payload;

    dispatch(set({ section, isSaving: true }));

    const requestData = files;

    const promise = createAjaxRequest({
      url: '/episodeFile/bulk',
      method: 'PUT',
      dataType: 'json',
      data: JSON.stringify(requestData)
    }).request;

    promise.done((data) => {
      dispatch(batchActions([
        ...files.map((file) => {
          const id = file.id;
          const props = {};
          const episodeFile = data.find((f) => f.id === id);

          props.qualityCutoffNotMet = episodeFile.qualityCutoffNotMet;
          props.customFormats = episodeFile.customFormats;
          props.customFormatScore = episodeFile.customFormatScore;
          props.languages = file.languages;
          props.quality = file.quality;
          props.releaseGroup = file.releaseGroup;
          props.indexerFlags = file.indexerFlags;

          return updateItem({
            section,
            id,
            ...props
          });
        }),

        set({
          section,
          isSaving: false,
          saveError: null
        })
      ]));
    });

    promise.fail((xhr) => {
      dispatch(set({
        section,
        isSaving: false,
        saveError: xhr
      }));
    });
  },

  [MOVE_EPISODE_FILE]: function(getState, payload, dispatch) {
    const {
      id: episodeFileId,
      newPath
    } = payload;

    dispatch(set({ section, isSaving: true }));

    const promise = createAjaxRequest({
      url: `/episodeFile/${episodeFileId}/move`,
      method: 'PUT',
      dataType: 'json',
      data: JSON.stringify({ newPath })
    }).request;

    promise.done((data) => {
      dispatch(batchActions([
        updateItem({
          section,
          id: episodeFileId,
          path: data.path
        }),

        set({
          section,
          isSaving: false,
          saveError: null
        })
      ]));
    });

    promise.fail((xhr) => {
      dispatch(set({
        section,
        isSaving: false,
        saveError: xhr
      }));
    });
  },

  [ASSOCIATE_EPISODE_FILE]: function(getState, payload, dispatch) {
    const {
      episodeId,
      filePath
    } = payload;

    dispatch(set({ section, isSaving: true }));

    const promise = createAjaxRequest({
      url: `/episodeFile/${episodeId}/associate`,
      method: 'PUT',
      dataType: 'json',
      data: JSON.stringify({ filePath })
    }).request;

    promise.done((data) => {
      // Get the episode from state to update it with the new episodeFileId
      const episodes = getState().episodes.items;
      const episode = _.find(episodes, { id: episodeId });

      dispatch(batchActions([
        updateItem({
          section,
          id: data.id,
          path: data.path,
          episodeFileId: data.id
        }),

        // Update the episode with the new episodeFileId so the UI reflects the association
        episode && updateItem({
          section: 'episodes',
          ...episode,
          episodeFileId: data.id,
          hasFile: true
        }),

        set({
          section,
          isSaving: false,
          saveError: null
        })
      ]).filter(Boolean)); // filter out undefined/null items
    });

    promise.fail((xhr) => {
      dispatch(set({
        section,
        isSaving: false,
        saveError: xhr
      }));
    });
  },

  [UNASSOCIATE_EPISODE_FILE]: function(getState, payload, dispatch) {
    const {
      episodeId
    } = payload;

    dispatch(set({ section, isSaving: true }));

    const promise = createAjaxRequest({
      url: `/episodeFile/${episodeId}/unassociate`,
      method: 'DELETE',
      dataType: 'json'
    }).request;

    promise.done(() => {
      // Get the episode from state to update it after unassociation
      const episodes = getState().episodes.items;
      const episode = _.find(episodes, { id: episodeId });
      const episodeFileId = episode?.episodeFileId;

      const actions = [];
      
      // Remove the episodeFile from Redux state
      if (episodeFileId) {
        actions.push(removeItem({ section, id: episodeFileId }));
      }
      
      // Update the episode to remove the episodeFileId
      if (episode) {
        actions.push(updateItem({
          id: episode.id,
          section: 'episodes',
          episodeFileId: 0,
          hasFile: false
        }));
      }
      
      // Always add the set action
      actions.push(set({
        section,
        isSaving: false,
        saveError: null
      }));

      dispatch(batchActions(actions));
    });

    promise.fail((xhr) => {
      dispatch(set({
        section,
        isSaving: false,
        saveError: xhr
      }));
    });
  }
});

//
// Reducers

export const reducers = createHandleActions({

  [CLEAR_EPISODE_FILES]: (state) => {
    return Object.assign({}, state, defaultState);
  }

}, defaultState, section);
